using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

public class RepositorySignJob(
    ApkgDbContext db,
    IGpgSigningService signingService,
    ILogger<RepositorySignJob> logger) : IBackgroundJob
{
    public string Name => "Sign Pending bucket and swap";

    public string Description => "Signs all pending buckets with their configured GPG certificate, then atomically swaps them into the live slot (PrimaryBucketId). Until this job completes, pending buckets are invisible to APT clients.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("RepositorySignJob started.");

        var repos = await db.AptRepositories
            .Include(r => r.Certificate)
            .Where(r => r.SecondaryBucketId != null)
            .ToListAsync();

        foreach (var repo in repos)
        {
            try
            {
                await SignAndPromoteRepositoryAsync(repo);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sign repository {RepoName}", repo.Name);
            }
        }

        logger.LogInformation("RepositorySignJob finished.");
    }

    private async Task SignAndPromoteRepositoryAsync(AptRepository repo)
    {
        var bucketEntity = await db.AptBuckets.FindAsync(repo.SecondaryBucketId);
        if (bucketEntity == null)
        {
            // Bucket was deleted externally (should never happen after GC fix, but defend anyway).
            // Clear the dangling FK so the repo doesn't remain permanently stuck.
            logger.LogWarning(
                "Repository {RepoName}: pending bucket {BucketId} no longer exists. Clearing dangling SecondaryBucketId.",
                repo.Name, repo.SecondaryBucketId);
            repo.SecondaryBucketId = null;
            db.AptRepositories.Update(repo);
            await db.SaveChangesAsync();
            return;
        }

        if (string.IsNullOrEmpty(bucketEntity.ReleaseContent))
        {
            logger.LogWarning("Repository {RepoName} pending bucket {BucketId} has no Release content. Skipping.", repo.Name, repo.SecondaryBucketId);
            return;
        }

        if (repo.EnableGpgSign && repo.Certificate != null)
        {
            logger.LogInformation("Signing repository {RepoName} with certificate {CertName}...", repo.Name, repo.Certificate.FriendlyName);
            bucketEntity.InReleaseContent = await signingService.SignClearsignAsync(bucketEntity.ReleaseContent, repo.Certificate.PrivateKey);
            bucketEntity.SignedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        else
        {
            logger.LogInformation("Repository {RepoName} signing is disabled or no certificate found. Skipping signing.", repo.Name);
        }

        // Atomically promote: only now is the signed bucket exposed to apt clients
        repo.PrimaryBucketId = repo.SecondaryBucketId;
        repo.SecondaryBucketId = null;
        db.AptRepositories.Update(repo);
        await db.SaveChangesAsync();

        logger.LogInformation("Repository {RepoName} is now live with signed bucket {BucketId}.", repo.Name, repo.PrimaryBucketId);
    }
}
