using System.Security.Cryptography;
using System.Text;
using System.IO.Compression;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class RepositorySyncJob(
    TemplateDbContext db,
    AptMetadataService metadataService,
    IGpgSigningService signingService,
    FeatureFoldersProvider folders,
    ILogger<RepositorySyncJob> logger) : IBackgroundJob
{
    private string BucketsRoot => Path.Combine(folders.GetWorkspaceFolder(), "Buckets");

    public string Name => "APT Repository Sync V2";

    public string Description => "Processes and signs packages from mirrors into repository-specific buckets.";

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

        logger.LogInformation("RepositorySyncJob V2 finished.");
    }

    private async Task SyncAndSignRepositoryAsync(AptRepository repo)
    {
        logger.LogInformation("Processing and signing repository {RepoName}...", repo.Name);

        // 1. Create a new bucket (if needed) or work with existing one
        // For now, we still create a new bucket to ensure atomicity of the update
        var newBucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptBuckets.Add(newBucket);
        await db.SaveChangesAsync();

        var newBucketId = newBucket.Id;
        List<AptPackage> packages;

        // 2. Data Transfer
        if (repo.MirrorId != null)
        {
            if (repo.Mirror?.CurrentBucketId == null)
            {
                logger.LogWarning("Repository {RepoName} is linked to mirror {MirrorSuite} which has no active bucket. Skipping data copy.", repo.Name, repo.Mirror?.Suite);
                packages = new List<AptPackage>();
            }
            else
            {
                var mirrorBucketId = repo.Mirror.CurrentBucketId.Value;
                logger.LogInformation("Copying packages from Mirror Bucket {MirrorBucketId} to New Bucket {NewBucketId}...", mirrorBucketId, newBucketId);
                
                packages = await db.AptPackages
                    .AsNoTracking()
                    .Where(p => p.BucketId == mirrorBucketId)
                    .ToListAsync();

                foreach (var pkg in packages)
                {
                    pkg.Id = 0; 
                    pkg.BucketId = newBucketId;
                    db.AptPackages.Add(pkg);
                }
                await db.SaveChangesAsync();
            }
        }
        else
        {
            // Standalone repository. 
            // In the future, we might copy from repo.CurrentBucket to newBucket if we want to preserve manually added packages.
            // For now, let's just see if there's anything to copy.
            if (repo.CurrentBucketId != null)
            {
                logger.LogInformation("Standalone repository {RepoName}: Copying packages from Current Bucket {CurrentBucketId} to New Bucket {NewBucketId}...", repo.Name, repo.CurrentBucketId, newBucketId);
                packages = await db.AptPackages
                    .AsNoTracking()
                    .Where(p => p.BucketId == repo.CurrentBucketId)
                    .ToListAsync();
                
                foreach (var pkg in packages)
                {
                    pkg.Id = 0;
                    pkg.BucketId = newBucketId;
                    db.AptPackages.Add(pkg);
                }
                await db.SaveChangesAsync();
            }
            else
            {
                packages = new List<AptPackage>();
            }
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
                var compPackages = packages
                    .Where(p => p.Component == component && (p.Architecture == arch || p.Architecture == "all"))
                    .ToList();
                var pkgsContent = metadataService.GeneratePackagesFile(compPackages);
                var pkgsBytes = Encoding.UTF8.GetBytes(pkgsContent);

                // Write Packages to disk
                var relativePath = $"{component}/binary-{arch}";
                var packageDir = Path.Combine(BucketsRoot, newBucketId.ToString(), relativePath);
                Directory.CreateDirectory(packageDir);

                var packagesPath = Path.Combine(packageDir, "Packages");
                await File.WriteAllBytesAsync(packagesPath, pkgsBytes);

                var sha256 = BitConverter.ToString(SHA256.HashData(pkgsBytes)).Replace("-", "").ToLower();
                
                // Add entry for raw Packages
                releaseSb.AppendLine($" {sha256} {pkgsBytes.Length} {relativePath}/Packages");
                
                // Write Packages.gz to disk
                var gzPath = packagesPath + ".gz";
                using var ms = new MemoryStream();
                await using (var gs = new GZipStream(ms, CompressionLevel.Optimal))
                {
                    await gs.WriteAsync(pkgsBytes);
                }
                var gzBytes = ms.ToArray();
                await File.WriteAllBytesAsync(gzPath, gzBytes);

                var gzSha256 = BitConverter.ToString(SHA256.HashData(gzBytes)).Replace("-", "").ToLower();
                releaseSb.AppendLine($" {gzSha256} {gzBytes.Length} {relativePath}/Packages.gz");
            }
        }

        var releaseContent = releaseSb.ToString();
        newBucket.ReleaseContent = releaseContent;

        // 4. GPG Sign
        if (repo.Certificate != null)
        {
            logger.LogInformation("Signing with certificate {CertName}...", repo.Certificate.FriendlyName);
            newBucket.InReleaseContent = await signingService.SignClearsignAsync(releaseContent, repo.Certificate.PrivateKey);
        }
        else
        {
            logger.LogWarning("No certificate configured for repository {RepoName}. InRelease will be empty.", repo.Name);
        }

        // 5. Commit and Swap
        await db.SaveChangesAsync();

        db.AptRepositories.Update(repo);
        repo.CurrentBucketId = newBucketId;
        await db.SaveChangesAsync();
        
        db.ChangeTracker.Clear();
        logger.LogInformation("Repository {RepoName} is now live with Bucket {BucketId}.", repo.Name, newBucketId);
    }
}
