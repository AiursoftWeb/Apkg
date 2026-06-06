using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Services.Contents;
using Aiursoft.Canon.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class RepositorySyncJob(
    ApkgDbContext db,
    AptMetadataService metadataService,
    FeatureFoldersProvider folders,
    ILogger<RepositorySyncJob> logger,
    DebResolutionService debResolution) : IBackgroundJob
{
    private string BucketsRoot => folders.GetBucketsFolder();

    public string Name => "Seed All APT repository in pending bucket.";

    public string Description => "Fetches packages from all configured mirrors and builds new pending buckets. Does NOT swap them live — triggers 'Sign Pending bucket and swap' to sign and promote.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("RepositorySyncJob started.");

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

        logger.LogInformation("RepositorySyncJob finished. Pending buckets are staged; RepositorySignJob will sign and promote them.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Orchestrator
    // ═══════════════════════════════════════════════════════════════════════

    private async Task SyncAndSignRepositoryAsync(AptRepository repo)
    {
        logger.LogInformation("Processing and signing repository {RepoName}...", repo.Name);

        var (bucketId, realHashes) = await CreateBucketAndLoadRealHashes(repo);

        if (repo.MirrorId != null && repo.Mirror?.PrimaryBucketId != null)
            await CopyMirrorPackagesAsync(repo.Mirror.PrimaryBucketId.Value, bucketId, realHashes);

        await MergeLocalPackagesAsync(repo.Id, bucketId, repo.Suite);

        var (architectures, components) = ParseArchComponents(repo);
        var releaseContent = await BuildReleaseMetadata(bucketId, repo.Suite, architectures, components);

        await StoreReleaseContent(bucketId, releaseContent);

        db.ChangeTracker.Clear();
        logger.LogInformation("Repository {RepoName} bucket {BucketId} is staged and awaiting signing.", repo.Name, bucketId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Step 1 — Bucket creation
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<(int BucketId, HashSet<string> RealHashes)> CreateBucketAndLoadRealHashes(
        AptRepository repo)
    {
        var realHashes = await LoadRealHashes(repo.PrimaryBucketId);

        var newBucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptRepositories.Update(repo);
        repo.SecondaryBucket = newBucket;
        await db.SaveChangesAsync();

        return (newBucket.Id, realHashes);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pure helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Parses <c>repo.Architecture</c> and <c>repo.Components</c> into arrays.
    /// </summary>
    private static (string[] Architectures, string[] Components) ParseArchComponents(AptRepository repo)
    {
        var architectures = repo.Architecture.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var components = repo.Components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (architectures, components);
    }

    /// <summary>
    /// Builds the Release file header lines (everything before the SHA256 file list).
    /// </summary>
    private static string BuildReleasePreamble(string suite, string[] architectures, string[] components)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Origin: Aiursoft Apkg");
        sb.AppendLine("Label: Aiursoft Apkg");
        sb.AppendLine($"Suite: {suite}");
        sb.AppendLine($"Codename: {suite}");
        sb.AppendLine($"Date: {DateTime.UtcNow:R}");
        sb.AppendLine($"Architectures: {string.Join(" ", architectures)}");
        sb.AppendLine($"Components: {string.Join(" ", components)}");
        sb.AppendLine("SHA256:");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a single Release file SHA256 entry:
    /// <c> {sha256} {size} {relativePath}</c>
    /// </summary>
    private static string BuildReleaseFileEntry(string sha256, long size, string relativePath)
    {
        return $" {sha256} {size} {relativePath}";
    }

    /// <summary>
    /// Maps an <see cref="ApkgDebPackage"/> to an <see cref="AptPackage"/> entity.
    /// Pure transformation — no side effects.
    /// </summary>
    private static AptPackage MapLocalToAptPackage(ApkgDebPackage lp, int bucketId, string repoSuite, string component)
    {
        return new AptPackage
        {
            BucketId = bucketId,
            Component = component,
            OriginSuite = repoSuite,
            OriginComponent = component,
            Package = lp.Package,
            Version = lp.Version,
            Architecture = lp.Architecture,
            Maintainer = lp.Maintainer,
            OriginalMaintainer = lp.OriginalMaintainer,
            Description = lp.Description ?? string.Empty,
            DescriptionMd5 = string.Empty,
            Section = lp.Section ?? string.Empty,
            Priority = lp.Priority ?? string.Empty,
            Origin = "ApkgDebPackage",
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
        };
    }

    /// <summary>
    /// Rewrites a pool path to include the suite name, e.g.
    /// "pool/main/g/pkg/pkg_1.0_all.deb" → "questing-addon/pool/main/g/pkg/pkg_1.0_all.deb".
    /// </summary>
    private static string RewritePoolFilenameForSuite(string filename, string suite)
    {
        const string prefix = "pool/";
        if (!string.IsNullOrWhiteSpace(filename) && filename.StartsWith(prefix))
            return $"{suite}/{filename}";
        return filename;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // I/O operations
    // ═══════════════════════════════════════════════════════════════════════

    private async Task<HashSet<string>> LoadRealHashes(int? primaryBucketId)
    {
        if (primaryBucketId == null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var realHashes = await db.AptPackages
            .AsNoTracking()
            .Where(p => p.BucketId == primaryBucketId.Value && !p.IsVirtual)
            .Select(p => p.SHA256)
            .Distinct()
            .ToListAsync();

        var set = new HashSet<string>(realHashes, StringComparer.OrdinalIgnoreCase);
        if (set.Count > 0)
            logger.LogInformation("Found {Count} already-materialized SHA256s in current primary.", set.Count);

        return set;
    }

    /// <summary>
    /// Batch-copies packages from the mirror bucket into the new bucket.
    /// Packages whose SHA256 matches a previously-materialized hash are
    /// kept as IsVirtual=false (their CAS file is already on disk).
    /// </summary>
    private async Task CopyMirrorPackagesAsync(
        int mirrorBucketId, int newBucketId, HashSet<string> realHashes)
    {
        logger.LogInformation("Copying packages from Mirror Bucket {MirrorBucketId} to New Bucket {NewBucketId}...",
            mirrorBucketId, newBucketId);

        var batchBuffer = new List<AptPackage>(1000);

        await foreach (var pkg in db.AptPackages
            .AsNoTracking()
            .Where(p => p.BucketId == mirrorBucketId)
            .AsAsyncEnumerable())
        {
            pkg.Id = 0;
            pkg.BucketId = newBucketId;
            if (realHashes.Contains(pkg.SHA256))
                pkg.IsVirtual = false;

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

    /// <summary>
    /// Merges enabled ApkgDebPackages into the new bucket, replacing
    /// upstream entries that share the same (Package, Architecture).
    /// </summary>
    private async Task MergeLocalPackagesAsync(int repoId, int newBucketId, string repoSuite)
    {
        var allLocalPackages = await db.ApkgDebPackages
            .AsNoTracking()
            .Include(lp => lp.ApkgRevision).ThenInclude(r => r!.ApkgPackage)
            .Where(lp => lp.RepositoryId == repoId && lp.IsEnabled)
            .ToListAsync();

        var localPackages = debResolution.ResolveWinningDebs(allLocalPackages);
        if (localPackages.Count == 0) return;

        logger.LogInformation("Merging {Count} local packages (from {Total} total) into Bucket {BucketId}...",
            localPackages.Count, allLocalPackages.Count, newBucketId);

        // Remove upstream packages that conflict with a winning local
        foreach (var lp in localPackages)
        {
            var toRemove = db.AptPackages
                .Where(p => p.BucketId == newBucketId && p.Package == lp.Package && p.Architecture == lp.Architecture);
            db.AptPackages.RemoveRange(toRemove);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Insert winning locals
        foreach (var lp in localPackages)
        {
            var component = lp.ApkgRevision?.ApkgPackage?.Component ?? "main";
            db.AptPackages.Add(MapLocalToAptPackage(lp, newBucketId, repoSuite, component));
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Builds the full Release metadata content by generating Packages and Contents
    /// files for every arch×component combination.
    /// </summary>
    private async Task<string> BuildReleaseMetadata(
        int bucketId, string suite, string[] architectures, string[] components)
    {
        logger.LogInformation("Generating metadata for Bucket {BucketId}...", bucketId);

        var sb = new StringBuilder(BuildReleasePreamble(suite, architectures, components));

        foreach (var arch in architectures)
        foreach (var component in components)
        {
            var (pkgRaw, pkgRawSz, pkgGz, pkgGzSz) =
                await WritePackagesFileAsync(bucketId, arch, component, suite);

            sb.AppendLine(BuildReleaseFileEntry(pkgRaw, pkgRawSz, $"{component}/binary-{arch}/Packages"));
            sb.AppendLine(BuildReleaseFileEntry(pkgGz, pkgGzSz, $"{component}/binary-{arch}/Packages.gz"));

            var (cntRaw, cntRawSz, cntGz, cntGzSz) =
                await WriteContentsFileAsync(bucketId, arch, component);

            sb.AppendLine(BuildReleaseFileEntry(cntRaw, cntRawSz, $"{component}/Contents-{arch}"));
            sb.AppendLine(BuildReleaseFileEntry(cntGz, cntGzSz, $"{component}/Contents-{arch}.gz"));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes Packages + Packages.gz for a single (arch, component) pair.
    /// Returns SHA256 and size for both the raw and gzipped files.
    /// </summary>
    private async Task<(string RawSha256, long RawSize, string GzSha256, long GzSize)>
        WritePackagesFileAsync(int bucketId, string arch, string component, string suite)
    {
        var packageDir = Path.Combine(BucketsRoot, bucketId.ToString(), $"{component}/binary-{arch}");
        Directory.CreateDirectory(packageDir);

        var packagesPath = Path.Combine(packageDir, "Packages");
        var gzPath = packagesPath + ".gz";

        string rawSha256;
        long rawSize;
        string gzSha256;
        long gzSize;

        await using (var rawFs = new FileStream(packagesPath, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var gzFs = new FileStream(gzPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var rawHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var gzHasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await using (var rawHashing = new HashingStream(rawFs, rawHasher))
            await using (var gzHashing = new HashingStream(gzFs, gzHasher))
            await using (var gzipStream = new GZipStream(gzHashing, CompressionLevel.Optimal))
            {
                var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                var rawWriter = new StreamWriter(rawHashing, utf8NoBom, leaveOpen: true);
                var gzWriter = new StreamWriter(gzipStream, utf8NoBom, leaveOpen: true);

                await foreach (var pkg in db.AptPackages
                    .AsNoTracking()
                    .Where(p => p.BucketId == bucketId && p.Component == component
                        && (p.Architecture == arch || p.Architecture == "all"))
                    .AsAsyncEnumerable())
                {
                    var filename = RewritePoolFilenameForSuite(pkg.Filename, suite);
                    await metadataService.WritePackageEntryAsync(rawWriter, pkg, filenameOverride: filename);
                    await metadataService.WritePackageEntryAsync(gzWriter, pkg, filenameOverride: filename);
                }

                await rawWriter.FlushAsync();
                await gzWriter.FlushAsync();
            }

            rawSha256 = BitConverter.ToString(rawHasher.GetHashAndReset()).Replace("-", "").ToLower();
            gzSha256 = BitConverter.ToString(gzHasher.GetHashAndReset()).Replace("-", "").ToLower();
            rawSize = rawFs.Length;
            gzSize = gzFs.Length;
        }

        return (rawSha256, rawSize, gzSha256, gzSize);
    }

    /// <summary>
    /// Writes Contents-{arch} + Contents-{arch}.gz for a single (arch, component) pair.
    /// </summary>
    private async Task<(string RawSha256, long RawSize, string GzSha256, long GzSize)>
        WriteContentsFileAsync(int bucketId, string arch, string component)
    {
        var contentsDir = Path.Combine(BucketsRoot, bucketId.ToString(), component);
        Directory.CreateDirectory(contentsDir);

        var pkgs = await db.AptPackages
            .AsNoTracking()
            .Where(p => p.BucketId == bucketId && p.Component == component
                && (p.Architecture == arch || p.Architecture == "all"))
            .Select(p => new { p.SHA256, p.Package, p.Section })
            .ToListAsync();

        var objectsRoot = folders.GetObjectsFolder();
        var contentsPackages = pkgs
            .Select(p => new ContentsPackage(
                Path.Combine(objectsRoot, p.SHA256[..2], $"{p.SHA256}.deb"),
                p.Package,
                p.Section))
            .ToList();

        var tempDir = Path.Combine(contentsDir, "_contents-tmp");
        var result = await ContentsGeneratorService.GenerateContentsFilesAsync(
            tempDir, arch, contentsDir, contentsPackages);

        try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
        catch { /* best-effort */ }

        return (result.RawSha256, result.RawSize, result.GzSha256, result.GzSize);
    }

    /// <summary>
    /// Saves the Release content to the bucket entity in the database.
    /// </summary>
    private async Task StoreReleaseContent(int bucketId, string releaseContent)
    {
        var bucket = await db.AptBuckets.FindAsync(bucketId);
        if (bucket != null)
        {
            bucket.ReleaseContent = releaseContent;
            await db.SaveChangesAsync();
        }
    }
}
