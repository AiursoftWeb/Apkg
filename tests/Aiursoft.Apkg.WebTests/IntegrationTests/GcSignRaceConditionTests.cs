using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Regression tests for the race condition between GarbageCollectionJob and
/// RepositorySignJob that caused "empty repo" failures in production.
///
/// Root cause (fixed):
///   GarbageCollectionJob only included CurrentBucketId in its "active" set.
///   PendingBucketId buckets (not yet promoted) were classified as orphaned and deleted.
///
/// This caused two failure modes:
///
///   Mode A — GC wins the race:
///     GC deletes the pending bucket before SignJob runs.
///     SignJob finds PendingBucketId pointing to nothing → skips promotion.
///     Result: CurrentBucketId remains null → repo invisible to apt clients.
///
///   Mode B — SignJob wins the race:
///     SignJob promotes the pending bucket to CurrentBucketId.
///     GC then tries to delete that bucket (its stale delete-list computed
///     before SignJob ran still included it).
///     Result: MySqlException FK constraint violation; job fails noisily.
///
/// The fix: GarbageCollectionJob now unions PendingBucketId values into its
/// active-bucket set so staged buckets are never treated as orphaned.
/// A secondary defensive fix in SignJob clears dangling PendingBucketId
/// references so a repo cannot get permanently stuck.
/// </summary>
[TestClass]
public class GcSignRaceConditionTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task CreateServer()
    {
        await base.CreateServer();
        _db = GetService<ApkgDbContext>();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private AptBucket CreateFinishedBucket(string origin = "Test", DateTime? createdAt = null)
    {
        var b = new AptBucket
        {
            CreatedAt = createdAt ?? DateTime.UtcNow,
            ReleaseContent = $"Origin: {origin}\nSuite: test\n"
        };
        _db.AptBuckets.Add(b);
        _db.SaveChanges();
        return b;
    }

    // ── Mode A regression: GC runs while PendingBucketId is set ──────

    /// <summary>
    /// Core regression for Mode A.
    /// Simulates: SyncJob finishes (sets PendingBucketId) and then GC fires
    /// before SignJob has a chance to run.
    ///
    /// The pending bucket MUST survive so SignJob can still promote it.
    /// Pre-fix behaviour: GC deleted the bucket → repo permanently empty.
    /// </summary>
    [TestMethod]
    public async Task GC_WhenPendingBucketExists_MustNotDeleteIt()
    {
        // Arrange: repo with CurrentBucketId=null, PendingBucketId=B
        var repo = _db.AptRepositories.First();
        var pending = CreateFinishedBucket();
        repo.CurrentBucketId = null;
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        // Act: GC fires before SignJob (the critical race window)
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert: the pending bucket must be completely untouched
        _db.ChangeTracker.Clear();
        var bucketStillExists = await _db.AptBuckets.AnyAsync(b => b.Id == pending.Id);
        Assert.IsTrue(bucketStillExists,
            "GC must NOT delete a bucket referenced by PendingBucketId. " +
            "If deleted, SignJob cannot promote it and the repo becomes permanently empty.");

        // The repo must still reference the pending bucket
        var updatedRepo = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.AreEqual(pending.Id, updatedRepo!.PendingBucketId,
            "PendingBucketId must be preserved so SignJob can still promote it.");
    }

    /// <summary>
    /// Verifies that after GC protects the pending bucket, SignJob can still
    /// sign and promote it — completing the full Mode A scenario safely.
    /// </summary>
    [TestMethod]
    public async Task GC_ThenSignJob_FullModeAScenario_RepoBecomesLive()
    {
        // Arrange
        var repo = await _db.AptRepositories.FirstAsync(r => r.CertificateId != null);
        var pending = CreateFinishedBucket();
        repo.CurrentBucketId = null;
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        // Act Step 1: GC fires (race window — should be a no-op for pending bucket)
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Act Step 2: SignJob runs after GC
        var signJob = GetService<RepositorySignJob>();
        await signJob.ExecuteAsync();

        // Assert: repo is now live with the signed bucket
        _db.ChangeTracker.Clear();
        var finalRepo = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.AreEqual(pending.Id, finalRepo!.CurrentBucketId,
            "After GC + SignJob, the pending bucket must be promoted to CurrentBucketId.");
        Assert.IsNull(finalRepo.PendingBucketId,
            "PendingBucketId must be cleared after successful promotion.");

        var liveBucket = await _db.AptBuckets.FindAsync(pending.Id);
        Assert.IsNotNull(liveBucket!.InReleaseContent,
            "The live bucket must have been GPG-signed.");
        Assert.IsTrue(liveBucket.InReleaseContent!.Contains("-----BEGIN PGP SIGNED MESSAGE-----"),
            "The InReleaseContent must be a valid clearsigned message.");
    }

    // ── Mode B regression: SignJob promotes while GC's delete-list is stale ─

    /// <summary>
    /// Core regression for Mode B.
    /// GC must not delete a bucket that is CurrentBucketId even when that
    /// bucket was PendingBucketId at the moment GC computed its orphan list.
    ///
    /// Simulated by running GC with PendingBucketId set, then manually promoting
    /// (mimicking SignJob), then running GC again — the bucket must survive.
    /// </summary>
    [TestMethod]
    public async Task GC_AfterSignJobPromotes_DoesNotDeleteNewCurrentBucket()
    {
        // Arrange: repo with old CurrentBucketId=A, new PendingBucketId=B
        var repo = await _db.AptRepositories.FirstAsync(r => r.CertificateId != null);
        var oldBucket = CreateFinishedBucket("old");
        var pending   = CreateFinishedBucket("new");
        repo.CurrentBucketId = oldBucket.Id;
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        // Act Step 1: GC fires (stale delete-list was computed here in the old bug)
        // With the fix, B (pending) must already be in the active set at query time.
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Act Step 2: SignJob promotes B → CurrentBucketId, A → orphaned
        var signJob = GetService<RepositorySignJob>();
        await signJob.ExecuteAsync();

        // Assert: B (now live) must not have been deleted
        _db.ChangeTracker.Clear();
        var liveRepo   = await _db.AptRepositories.FindAsync(repo.Id);
        var liveBucket = await _db.AptBuckets.FindAsync(pending.Id);

        Assert.AreEqual(pending.Id, liveRepo!.CurrentBucketId,
            "The promoted bucket must be CurrentBucketId.");
        Assert.IsNotNull(liveBucket,
            "The promoted bucket must still exist — GC must not have deleted it.");
    }

    // ── Full lifecycle with GC clean-up of old bucket ─────────────────

    /// <summary>
    /// End-to-end lifecycle test covering the correct behaviour at every step:
    ///
    ///  1. Repo starts with CurrentBucketId=A (old).
    ///  2. SyncJob analogue: PendingBucketId=B is set.
    ///  3. GC fires: A and B must both survive (A=live, B=pending).
    ///  4. SignJob fires: B is promoted; A is demoted.
    ///  5. GC fires again: A must be deleted (truly orphaned), B must survive.
    /// </summary>
    [TestMethod]
    public async Task FullLifecycle_OldBucketDeletedByGcOnlyAfterDemotion()
    {
        // Arrange
        var repo = await _db.AptRepositories.FirstAsync(r => r.CertificateId != null);
        var oldBucket = CreateFinishedBucket("old-gen", createdAt: DateTime.UtcNow.AddHours(-3));
        var pending   = CreateFinishedBucket("new-gen");
        repo.CurrentBucketId = oldBucket.Id;
        repo.PendingBucketId = pending.Id;
        _db.SaveChanges();

        var gc      = GetService<GarbageCollectionJob>();
        var signJob = GetService<RepositorySignJob>();

        // Step 3: GC fires while both buckets are referenced
        await gc.ExecuteAsync();
        _db.ChangeTracker.Clear();
        Assert.IsTrue(await _db.AptBuckets.AnyAsync(b => b.Id == oldBucket.Id),
            "Step 3: old (current) bucket must survive GC.");
        Assert.IsTrue(await _db.AptBuckets.AnyAsync(b => b.Id == pending.Id),
            "Step 3: pending bucket must survive GC.");

        // Step 4: SignJob promotes pending → live
        await signJob.ExecuteAsync();
        _db.ChangeTracker.Clear();
        var afterSign = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.AreEqual(pending.Id, afterSign!.CurrentBucketId,
            "Step 4: pending bucket must be promoted to CurrentBucketId.");
        Assert.IsNull(afterSign.PendingBucketId,
            "Step 4: PendingBucketId must be cleared.");

        // Step 5: GC fires again — old bucket is now orphaned and must be collected
        await gc.ExecuteAsync();
        _db.ChangeTracker.Clear();
        Assert.IsFalse(await _db.AptBuckets.AnyAsync(b => b.Id == oldBucket.Id),
            "Step 5: old (now-orphaned) bucket must be deleted by GC.");
        Assert.IsTrue(await _db.AptBuckets.AnyAsync(b => b.Id == pending.Id),
            "Step 5: live bucket must survive GC.");
    }

    // ── GC still deletes genuinely orphaned buckets ───────────────────

    /// <summary>
    /// Sanity check: the fix must not make GC too conservative.
    /// Buckets that are neither CurrentBucketId nor PendingBucketId
    /// (i.e., truly orphaned) must still be collected.
    /// </summary>
    [TestMethod]
    public async Task GC_TrulyOrphanedBucket_IsDeleted()
    {
        // Arrange: bucket that has no reference from any repo — created 3 hours ago
        var orphan = CreateFinishedBucket("orphan", createdAt: DateTime.UtcNow.AddHours(-3));
        // Do NOT link it to any repo

        // Act
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert
        _db.ChangeTracker.Clear();
        var stillExists = await _db.AptBuckets.AnyAsync(b => b.Id == orphan.Id);
        Assert.IsFalse(stillExists,
            "A bucket with no CurrentBucketId or PendingBucketId reference must be deleted by GC.");
    }

    // ── Defensive cleanup in SignJob ──────────────────────────────────

    /// <summary>
    /// If (despite the GC fix) a pending bucket is somehow externally deleted
    /// before SignJob runs, SignJob must clean up the dangling PendingBucketId
    /// rather than leaving the repo permanently stuck.
    ///
    /// InMemory DB has no FK constraints, which lets us simulate this edge case.
    /// </summary>
    [TestMethod]
    public async Task SignJob_WhenPendingBucketAlreadyDeleted_ClearsDanglingReference()
    {
        // Arrange: set PendingBucketId to a non-existent bucket ID
        var repo = _db.AptRepositories.First();
        var previousCurrentId = repo.CurrentBucketId;
        const int phantomBucketId = 999_999;
        repo.PendingBucketId = phantomBucketId; // no AptBucket row with this ID
        _db.SaveChanges();

        // Act
        var signJob = GetService<RepositorySignJob>();
        await signJob.ExecuteAsync();

        // Assert: dangling reference must be cleared
        _db.ChangeTracker.Clear();
        var finalRepo = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.IsNull(finalRepo!.PendingBucketId,
            "SignJob must clear PendingBucketId when the referenced bucket no longer exists, " +
            "so the repo is not permanently stuck in a broken state.");

        // The existing CurrentBucketId must be untouched
        Assert.AreEqual(previousCurrentId, finalRepo.CurrentBucketId,
            "CurrentBucketId must not be changed when the pending bucket was already gone.");
    }
}
