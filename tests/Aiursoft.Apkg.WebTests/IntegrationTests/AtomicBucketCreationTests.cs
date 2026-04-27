using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Verifies the EF Core navigation-property pattern used by both SyncJobs:
///
///   entity.SecondaryBucket = new AptBucket { ... };
///   await db.SaveChangesAsync();
///
/// The critical guarantee: after a single SaveChanges the new bucket MUST have
/// a non-zero Id AND entity.SecondaryBucketId must equal that Id.
/// This means GC cannot see the bucket as an orphan at any point —
/// there is no two-phase window where the bucket exists without a reference.
///
/// We also run GC immediately after the single save to prove it cannot delete
/// the newly created bucket.
/// </summary>
[TestClass]
public class AtomicBucketCreationTests : TestBase
{
    private ApkgDbContext _db = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
    }

    // ── AptMirror ────────────────────────────────────────────────────

    /// <summary>
    /// Core EF guarantee for Mirror:
    /// After a single SaveChanges via navigation property,
    /// bucket.Id must be non-zero and mirror.SecondaryBucketId must equal bucket.Id.
    /// </summary>
    [TestMethod]
    public async Task Mirror_NavigationPropertySave_BucketIdAndForeignKeyAreConsistent()
    {
        // Arrange
        var mirror = await _db.AptMirrors.FirstAsync();

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptMirrors.Update(mirror);
        mirror.SecondaryBucket = bucket;

        // Act — single SaveChanges (the pattern used by MirrorSyncJob)
        await _db.SaveChangesAsync();

        // Assert: EF must have resolved INSERT order and set both values
        Assert.AreNotEqual(0, bucket.Id,
            "EF must have set bucket.Id after SaveChanges (INSERT happened).");
        Assert.AreEqual(bucket.Id, mirror.SecondaryBucketId,
            "mirror.SecondaryBucketId must equal the newly assigned bucket.Id.");

        // Confirm round-trip from DB
        _db.ChangeTracker.Clear();
        var reloaded = await _db.AptMirrors.FindAsync(mirror.Id);
        Assert.AreEqual(bucket.Id, reloaded!.SecondaryBucketId,
            "DB round-trip: SecondaryBucketId must be persisted correctly.");
    }

    /// <summary>
    /// After the single-save, running GC immediately must NOT delete the new bucket —
    /// it is referenced by SecondaryBucketId and therefore in the active set.
    /// </summary>
    [TestMethod]
    public async Task Mirror_AfterSingleSave_GcDoesNotDeleteNewBucket()
    {
        // Arrange
        var mirror = await _db.AptMirrors.FirstAsync();

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptMirrors.Update(mirror);
        mirror.SecondaryBucket = bucket;
        await _db.SaveChangesAsync(); // single atomic save

        var bucketId = bucket.Id;

        // Act — GC runs immediately after (simulates worst-case timing)
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert — bucket must survive because it is referenced by SecondaryBucketId
        _db.ChangeTracker.Clear();
        Assert.IsTrue(await _db.AptBuckets.AnyAsync(b => b.Id == bucketId),
            "GC must NOT delete a bucket that was linked via SecondaryBucketId " +
            "in the same single SaveChanges. There must be no orphan window.");
    }

    // ── AptRepository ────────────────────────────────────────────────

    /// <summary>
    /// Core EF guarantee for Repository:
    /// After a single SaveChanges via navigation property,
    /// bucket.Id must be non-zero and repo.SecondaryBucketId must equal bucket.Id.
    /// </summary>
    [TestMethod]
    public async Task Repo_NavigationPropertySave_BucketIdAndForeignKeyAreConsistent()
    {
        // Arrange
        var repo = await _db.AptRepositories.FirstAsync();

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptRepositories.Update(repo);
        repo.SecondaryBucket = bucket;

        // Act — single SaveChanges (the pattern used by RepositorySyncJob)
        await _db.SaveChangesAsync();

        // Assert
        Assert.AreNotEqual(0, bucket.Id,
            "EF must have set bucket.Id after SaveChanges (INSERT happened).");
        Assert.AreEqual(bucket.Id, repo.SecondaryBucketId,
            "repo.SecondaryBucketId must equal the newly assigned bucket.Id.");

        // Confirm round-trip from DB
        _db.ChangeTracker.Clear();
        var reloaded = await _db.AptRepositories.FindAsync(repo.Id);
        Assert.AreEqual(bucket.Id, reloaded!.SecondaryBucketId,
            "DB round-trip: SecondaryBucketId must be persisted correctly.");
    }

    /// <summary>
    /// After the single-save, running GC immediately must NOT delete the new bucket —
    /// it is referenced by SecondaryBucketId and therefore in the active set.
    /// </summary>
    [TestMethod]
    public async Task Repo_AfterSingleSave_GcDoesNotDeleteNewBucket()
    {
        // Arrange
        var repo = await _db.AptRepositories.FirstAsync();

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptRepositories.Update(repo);
        repo.SecondaryBucket = bucket;
        await _db.SaveChangesAsync(); // single atomic save

        var bucketId = bucket.Id;

        // Act — GC runs immediately after (simulates worst-case timing)
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert
        _db.ChangeTracker.Clear();
        Assert.IsTrue(await _db.AptBuckets.AnyAsync(b => b.Id == bucketId),
            "GC must NOT delete a bucket that was linked via SecondaryBucketId " +
            "in the same single SaveChanges. There must be no orphan window.");
    }

    /// <summary>
    /// Contrast test: deliberately uses the OLD two-save pattern to prove the
    /// orphan window EXISTS without the fix — and that GC would indeed delete the bucket.
    ///
    /// This test documents the exact bug that the navigation-property fix prevents.
    /// If this test ever starts FAILING (bucket survives GC), it means GC has been
    /// made more conservative again, which might re-introduce the old 2-hour grace period.
    /// </summary>
    [TestMethod]
    public async Task Repo_OldTwoSavePattern_GcCanDeleteBucketInWindow()
    {
        // Arrange: simulate the OLD buggy two-save pattern —
        // bucket is inserted in Save 1 with no SecondaryBucketId reference.

        // Save 1: INSERT bucket, SecondaryBucketId still null
        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptBuckets.Add(bucket);
        await _db.SaveChangesAsync(); // ← bucket exists but is unreferenced!

        var bucketId = bucket.Id;

        // GC fires in the window BEFORE Save 2 (this is the race condition)
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Save 2 (would have happened next in the old code, but bucket is already gone)
        // repo.SecondaryBucketId = bucketId; // too late

        // Assert: the bucket was deleted by GC — this is the EXPECTED bad outcome
        _db.ChangeTracker.Clear();
        Assert.IsFalse(await _db.AptBuckets.AnyAsync(b => b.Id == bucketId),
            "With the old two-save pattern, GC WILL delete the bucket in the window. " +
            "This test documents the vulnerability that the navigation-property fix prevents.");
    }
}
