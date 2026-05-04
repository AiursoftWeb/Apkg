using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Canon.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class RepositorySyncJob(
    ApkgDbContext db,
    AptMetadataService metadataService,
    FeatureFoldersProvider folders,
    ILogger<RepositorySyncJob> logger) : IBackgroundJob
{
    private string BucketsRoot => folders.GetBucketsFolder();

    public string Name => "Seed All APT repository in pending bucket.";

    public string Description => "Fetches packages from all configured mirrors and builds new pending buckets. Does NOT swap them live — triggers 'Sign Pending bucket and swap' to sign and promote.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("RepositorySyncJob V2 started.");

        var repos = await db.AptRepositories
            .Include(r => r.Mirror)
            .Include(r => r.Certificate)
            .ToListAsync();

        foreach (var repo in repos)
        {
            try
            {
                await SyncAndSignRepositoryAsync(repo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process and sign repository {RepoName}", repo.Name);
            }
        }

        logger.LogInformation("RepositorySyncJob V2 finished. Pending buckets are staged; RepositorySignJob will sign and promote them.");
    }

    private async Task SyncAndSignRepositoryAsync(AptRepository repo)
    {
        logger.LogInformation("Processing and signing repository {RepoName}...", repo.Name);

        // 1. Create a new bucket and immediately link it as SecondaryBucketId in a single
        //    SaveChanges call. Using the navigation property lets EF Core resolve the INSERT
        //    order automatically (INSERT bucket first, then UPDATE repo.SecondaryBucketId),
        //    eliminating any window where GC could delete the unreferenced new bucket.
        var newBucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptRepositories.Update(repo);
        repo.SecondaryBucket = newBucket;
        await db.SaveChangesAsync(); // atomic: bucket inserted + SecondaryBucketId updated in one round-trip

        var newBucketId = newBucket.Id;

        // Pre-load the SHA256 hashes of packages that are already materialized
        // (IsVirtual = false) in the current primary bucket. When a re-sync produces
        // a package with an identical SHA256, the CAS file is already on disk — there
        // is no need to re-download it, so we carry IsVirtual = false forward.
        // A package with a different SHA256 (new version) correctly remains virtual.
        var previouslyRealHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (repo.PrimaryBucketId != null)
        {
            var realHashes = await db.AptPackages
                .AsNoTracking()
                .Where(p => p.BucketId == repo.PrimaryBucketId.Value && !p.IsVirtual)
                .Select(p => p.SHA256)
                .Distinct()
                .ToListAsync();
            previouslyRealHashes = new HashSet<string>(realHashes, StringComparer.OrdinalIgnoreCase);
            if (previouslyRealHashes.Count > 0)
            {
                logger.LogInformation(
                    "Repository {RepoName}: found {Count} already-materialized SHA256s in current primary; these will remain IsVirtual=false if unchanged.",
                    repo.Name, previouslyRealHashes.Count);
            }
        }

        // 2. Data Transfer (Copy from Mirror Bucket to Repo Bucket)
        if (repo.MirrorId != null)
        {
            if (repo.Mirror?.PrimaryBucketId == null)
            {
                logger.LogWarning("Repository {RepoName} is linked to mirror {MirrorSuite} which has no active bucket. Skipping data copy.", repo.Name, repo.Mirror?.Suite);
            }
            else
            {
                var mirrorBucketId = repo.Mirror.PrimaryBucketId.Value;
                logger.LogInformation("Copying packages from Mirror Bucket {MirrorBucketId} to New Bucket {NewBucketId}...", mirrorBucketId, newBucketId);

                // Stream from DB and insert to avoid loading all in memory
                var query = db.AptPackages
                    .AsNoTracking()
                    .Where(p => p.BucketId == mirrorBucketId)
                    .AsAsyncEnumerable();

                var batchBuffer = new List<AptPackage>(1000);

                await foreach (var pkg in query)
                {
                    pkg.Id = 0;
                    pkg.BucketId = newBucketId;
                    // If this exact binary was already downloaded (same SHA256), the CAS
                    // file is still on disk. Preserve IsVirtual = false so the first
                    // subsequent request skips the lazy-sync entirely.
                    if (previouslyRealHashes.Contains(pkg.SHA256))
                    {
                        pkg.IsVirtual = false;
                    }
                    batchBuffer.Add(pkg);

                    if (batchBuffer.Count >= 1000)
                    {
                        db.AptPackages.AddRange(batchBuffer);
                        await db.SaveChangesAsync();
                        db.ChangeTracker.Clear();
                        batchBuffer.Clear();
                    }
                }

                if (batchBuffer.Count > 0)
                {
                    db.AptPackages.AddRange(batchBuffer);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                }
            }
        }
        else if (repo.PrimaryBucketId != null)
        {
            logger.LogInformation("Standalone repository {RepoName}: Copying packages from Current Bucket {PrimaryBucketId} to New Bucket {NewBucketId}...", repo.Name, repo.PrimaryBucketId, newBucketId);
            var query = db.AptPackages
                .AsNoTracking()
                .Where(p => p.BucketId == repo.PrimaryBucketId)
                .AsAsyncEnumerable();

            var batchBuffer = new List<AptPackage>(1000);

            await foreach (var pkg in query)
            {
                pkg.Id = 0;
                pkg.BucketId = newBucketId;
                batchBuffer.Add(pkg);

                if (batchBuffer.Count >= 1000)
                {
                    db.AptPackages.AddRange(batchBuffer);
                    await db.SaveChangesAsync();
                    db.ChangeTracker.Clear();
                    batchBuffer.Clear();
                }
            }

            if (batchBuffer.Count > 0)
            {
                db.AptPackages.AddRange(batchBuffer);
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
            }
        }

        // 2b. Merge LocalPackages: override all upstream (Package, Architecture) pairs
        var localPackages = await db.LocalPackages
            .AsNoTracking()
            .Where(lp => lp.RepositoryId == repo.Id && lp.IsEnabled)
            .ToListAsync();

        if (localPackages.Count > 0)
        {
            logger.LogInformation("Merging {Count} local packages into Bucket {BucketId}...", localPackages.Count, newBucketId);

            // Remove all upstream packages that conflict with a LocalPackage by (Package, Architecture)
            foreach (var lp in localPackages)
            {
                var toRemove = db.AptPackages
                    .Where(p => p.BucketId == newBucketId && p.Package == lp.Package && p.Architecture == lp.Architecture);
                db.AptPackages.RemoveRange(toRemove);
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            // Insert LocalPackages as AptPackages in the new bucket
            foreach (var lp in localPackages)
            {
                db.AptPackages.Add(new AptPackage
                {
                    BucketId = newBucketId,
                    Component = lp.Component,
                    OriginSuite = repo.Suite,
                    OriginComponent = lp.Component,
                    Package = lp.Package,
                    Version = lp.Version,
                    Architecture = lp.Architecture,
                    Maintainer = lp.Maintainer,
                    OriginalMaintainer = lp.OriginalMaintainer,
                    Description = lp.Description ?? string.Empty,
                    DescriptionMd5 = string.Empty,
                    Section = lp.Section ?? string.Empty,
                    Priority = lp.Priority ?? string.Empty,
                    Origin = "LocalPackage",
                    Bugs = string.Empty,
                    Homepage = lp.Homepage,
                    InstalledSize = lp.InstalledSize,
                    Depends = lp.Depends,
                    Recommends = lp.Recommends,
                    Suggests = lp.Suggests,
                    Conflicts = lp.Conflicts,
                    Breaks = lp.Breaks,
                    Replaces = lp.Replaces,
                    Provides = lp.Provides,
                    Source = lp.Source,
                    MultiArch = lp.MultiArch,
                    Filename = lp.Filename,
                    Size = lp.Size,
                    MD5sum = lp.MD5sum ?? string.Empty,
                    SHA1 = lp.SHA1 ?? string.Empty,
                    SHA256 = lp.SHA256,
                    SHA512 = lp.SHA512 ?? string.Empty,
                    IsVirtual = false,
                    RemoteUrl = null
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        // 3. Metadata Generation & Signing
        logger.LogInformation("Generating and signing metadata for Bucket {BucketId}...", newBucketId);

        var architectures = repo.Architecture.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var components = repo.Components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var releaseSb = new StringBuilder();
        releaseSb.AppendLine($"Origin: Aiursoft Apkg");
        releaseSb.AppendLine($"Label: Aiursoft Apkg");
        releaseSb.AppendLine($"Suite: {repo.Suite}");
        releaseSb.AppendLine($"Codename: {repo.Suite}");
        releaseSb.AppendLine($"Date: {DateTime.UtcNow:R}");
        releaseSb.AppendLine($"Architectures: {string.Join(" ", architectures)}");
        releaseSb.AppendLine($"Components: {string.Join(" ", components)}");
        releaseSb.AppendLine("SHA256:");

        foreach (var arch in architectures)
        {
            foreach (var component in components)
            {
                var relativePath = $"{component}/binary-{arch}";
                var packageDir = Path.Combine(BucketsRoot, newBucketId.ToString(), relativePath);
                Directory.CreateDirectory(packageDir);

                var packagesPath = Path.Combine(packageDir, "Packages");
                var gzPath = packagesPath + ".gz";

                string rawSha256;
                long rawSize;
                string gzSha256;
                long gzSize;

                // Open file streams
                await using (var rawFs = new FileStream(packagesPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var gzFs = new FileStream(gzPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var rawHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    using (var gzHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    {
                        // Use wrappers to hash while writing
                        await using (var rawHashingStream = new HashingStream(rawFs, rawHasher))
                        await using (var gzHashingStream = new HashingStream(gzFs, gzHasher))
                        await using (var gzipStream = new GZipStream(gzHashingStream, CompressionLevel.Optimal))
                        {
                            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                            var rawWriter = new StreamWriter(rawHashingStream, utf8NoBom, leaveOpen: true);
                            var gzWriter = new StreamWriter(gzipStream, utf8NoBom, leaveOpen: true);

                            var query = db.AptPackages
                                .AsNoTracking()
                                .Where(p => p.BucketId == newBucketId && p.Component == component && (p.Architecture == arch || p.Architecture == "all"))
                                .AsAsyncEnumerable();

                            await foreach (var pkg in query)
                            {
                                await metadataService.WritePackageEntryAsync(rawWriter, pkg);
                                await metadataService.WritePackageEntryAsync(gzWriter, pkg);
                            }

                            await rawWriter.FlushAsync();
                            await gzWriter.FlushAsync();
                        }
                        rawSha256 = BitConverter.ToString(rawHasher.GetHashAndReset()).Replace("-", "").ToLower();
                        gzSha256 = BitConverter.ToString(gzHasher.GetHashAndReset()).Replace("-", "").ToLower();
                    }
                    rawSize = rawFs.Length;
                    gzSize = gzFs.Length;
                }

                releaseSb.AppendLine($" {rawSha256} {rawSize} {relativePath}/Packages");
                releaseSb.AppendLine($" {gzSha256} {gzSize} {relativePath}/Packages.gz");
            }
        }

        var releaseContent = releaseSb.ToString();

        // Fetch newBucket again to avoid tracking issues
        var bucketEntity = await db.AptBuckets.FindAsync(newBucketId);
        if (bucketEntity != null)
        {
            bucketEntity.ReleaseContent = releaseContent;
            await db.SaveChangesAsync();
        }

        // SecondaryBucketId was already set at the top of this method; no change needed here.
        // RepositorySignJob will detect ReleaseContent != null and promote it.
        db.ChangeTracker.Clear();
        logger.LogInformation("Repository {RepoName} bucket {BucketId} is staged and awaiting signing.", repo.Name, newBucketId);
    }
}

internal class HashingStream(Stream baseStream, IncrementalHash hasher) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => baseStream.Length;
    public override long Position { get => baseStream.Position; set => throw new NotSupportedException(); }

    public override void Flush() => baseStream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => baseStream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count)
    {
        hasher.AppendData(buffer, offset, count);
        baseStream.Write(buffer, offset, count);
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        hasher.AppendData(buffer, offset, count);
        await baseStream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        hasher.AppendData(buffer.Span);
        await baseStream.WriteAsync(buffer, cancellationToken);
    }
}
