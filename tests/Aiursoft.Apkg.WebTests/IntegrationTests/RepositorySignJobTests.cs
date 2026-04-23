using System.Net;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Tests for RepositorySignJob focusing on the core safety invariant:
/// an unsigned bucket must never be served to APT clients.
///
/// The critical contract enforced by the PendingBucketId pattern:
///   RepositorySyncJob  → sets PendingBucketId (bucket is invisible to APT)
///   RepositorySignJob  → signs → then atomically sets CurrentBucketId = PendingBucketId
/// </summary>
[TestClass]
public class RepositorySignJobTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        _db = GetService<ApkgDbContext>();
    }

    private AptBucket CreatePendingBucket(string releaseContent = "Origin: Test\nSuite: test\n")
    {
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            BuildFinished = true,
            ReleaseContent = releaseContent
        };
        _db.AptBuckets.Add(bucket);
        _db.SaveChanges();
        return bucket;
    }

    // ──────────────────────────────────────────────────────────────
    // RepositorySignJob contract tests
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RepositorySignJob_WhenNoPendingBucket_LeavesCurrentBucketUnchanged()
    {
        // Arrange: repo has no pending bucket
        var repo = _db.AptRepositories.First();
        var originalCurrentBucketId = repo.CurrentBucketId;
        repo.PendingBucketId = null;
        _db.SaveChanges();

        // Act
        var job = GetService<RepositorySignJob>();
        await job.ExecuteAsync();

        // Assert: nothing changed
        _db.ChangeTracker.Clear();
        var updated = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.AreEqual(originalCurrentBucketId, updated!.CurrentBucketId,
            "CurrentBucketId must not change when there is no pending bucket.");
        Assert.IsNull(updated.PendingBucketId);
    }

    [TestMethod]
    public async Task RepositorySignJob_WithCertificate_SignsThenPromotes()
    {
        // Arrange: repo with a certificate and a pending bucket
        var repo = await _db.AptRepositories.FirstAsync(r => r.CertificateId != null);
        var pending = CreatePendingBucket();
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        // Act
        var job = GetService<RepositorySignJob>();
        await job.ExecuteAsync();

        // Assert
        _db.ChangeTracker.Clear();
        var updatedRepo   = await _db.AptRepositories.FindAsync(repo.Id);
        var updatedBucket = await _db.AptBuckets.FindAsync(pending.Id);

        Assert.AreEqual(pending.Id, updatedRepo!.CurrentBucketId,
            "The pending bucket must be promoted to CurrentBucketId after signing.");
        Assert.IsNull(updatedRepo.PendingBucketId,
            "PendingBucketId must be cleared once the bucket is promoted.");
        Assert.IsNotNull(updatedBucket!.InReleaseContent,
            "InReleaseContent must be written by the signing step.");
        Assert.IsTrue(updatedBucket.InReleaseContent!.Contains("-----BEGIN PGP SIGNED MESSAGE-----"),
            "InReleaseContent must be a valid clearsigned GPG message.");
        Assert.IsNotNull(updatedBucket.SignedAt,
            "SignedAt must be recorded when the bucket is signed.");
    }

    [TestMethod]
    public async Task RepositorySignJob_WithoutCertificate_PromotesBucketWithoutSigning()
    {
        // Arrange: repo without a certificate
        var repo = _db.AptRepositories.First();
        repo.CertificateId = null;
        var pending = CreatePendingBucket();
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        // Act
        var job = GetService<RepositorySignJob>();
        await job.ExecuteAsync();

        // Assert: bucket is promoted but not signed
        _db.ChangeTracker.Clear();
        var updatedRepo   = await _db.AptRepositories.FindAsync(repo.Id);
        var updatedBucket = await _db.AptBuckets.FindAsync(pending.Id);

        Assert.AreEqual(pending.Id, updatedRepo!.CurrentBucketId,
            "Even without a certificate the bucket must be promoted.");
        Assert.IsNull(updatedRepo.PendingBucketId);
        Assert.IsNull(updatedBucket!.InReleaseContent,
            "InReleaseContent must remain null when there is no certificate.");
        Assert.IsNull(updatedBucket.SignedAt,
            "SignedAt must remain null when no signing occurred.");
    }

    // ──────────────────────────────────────────────────────────────
    // APT endpoint safety invariant tests
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AptEndpoint_WithPendingBucketNotYetPromoted_ReturnsNotFound()
    {
        // Arrange: bucket is staged for signing but CurrentBucketId is still null
        var repo = _db.AptRepositories.First();
        repo.CurrentBucketId = null;
        repo.PendingBucketId = CreatePendingBucket().Id;
        _db.SaveChanges();

        // Act: APT client requests InRelease before sign job has run
        var response = await Http.GetAsync($"/{repo.Distro}/dists/{repo.Suite}/InRelease");

        // Assert: the unsigned, unswapped bucket must NOT be reachable
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode,
            "APT must return 404 for a repo whose bucket has not been promoted yet.");
    }

    [TestMethod]
    public async Task AptEndpoint_AfterSignJobPromotes_ServesSignedContent()
    {
        // Arrange: repo with a pending bucket (cert available from seed)
        var repo = await _db.AptRepositories.FirstAsync(r => r.CertificateId != null);
        repo.CurrentBucketId = null;
        repo.PendingBucketId = CreatePendingBucket().Id;
        _db.SaveChanges();

        // Pre-condition: endpoint returns 404 before the sign job runs
        var before = await Http.GetAsync($"/{repo.Distro}/dists/{repo.Suite}/InRelease");
        Assert.AreEqual(HttpStatusCode.NotFound, before.StatusCode,
            "Pre-condition failed: endpoint should be unreachable before sign job.");

        // Act: run the sign job
        var job = GetService<RepositorySignJob>();
        await job.ExecuteAsync();

        // Assert: endpoint now returns a GPG-signed InRelease
        var after = await Http.GetAsync($"/{repo.Distro}/dists/{repo.Suite}/InRelease");
        after.EnsureSuccessStatusCode();
        var content = await after.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("-----BEGIN PGP SIGNED MESSAGE-----"),
            "After promotion the InRelease content must be GPG-signed.");
    }
}
