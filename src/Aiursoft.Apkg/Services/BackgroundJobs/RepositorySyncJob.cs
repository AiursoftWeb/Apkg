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
            .Where(r => r.MirrorId != null)
            .ToListAsync();

        foreach (var repo in repos)
        {
            try
            {
                if (repo.Mirror?.CurrentBucketId == null)
                {
                    logger.LogWarning("Repository {RepoName} is linked to mirror {MirrorSuite} which has no active bucket. Skipping.", repo.Name, repo.Mirror?.Suite);
                    continue;
                }

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

        // 1. Create a new bucket
        var newBucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptBuckets.Add(newBucket);
        await db.SaveChangesAsync();

        // 2. Data Transfer (Copy from Mirror)
        var mirrorBucketId = repo.Mirror!.CurrentBucketId!.Value;
        var newBucketId = newBucket.Id;

        // Perform fast copy
        var packages = await db.AptPackages
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

        // 3. Metadata Generation & Signing
        logger.LogInformation("Generating and signing metadata for Bucket {BucketId}...", newBucketId);
        
        var architectures = repo.Mirror.Architecture.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var components = repo.Mirror.Components.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
                var compPackages = packages.Where(p => p.Component == component && p.Architecture == arch).ToList();
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
