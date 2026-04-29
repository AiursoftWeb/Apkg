using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Tests for GarbageCollectionJob's CAS (.deb) file cleanup behaviour.
///
/// The GC uses a two-phase approach for physical files:
///   Phase 1 – Delete orphaned buckets and all their AptPackage rows from DB.
///   Phase 2 – Scan ObjectsRoot for .deb files; delete any whose SHA256 is no
///              longer referenced by any AptPackage OR LocalPackage row.
///
/// Critical invariants:
///   • A CAS file whose hash is referenced by an active AptPackage MUST be preserved.
///   • A CAS file whose hash is referenced by a LocalPackage MUST be preserved,
///     even if no AptPackage row currently references it (the LocalPackage acts as
///     a "hold" so the file survives the next RepositorySyncJob).
///   • A CAS file with NO references (genuinely orphaned) MUST be deleted.
/// </summary>
[TestClass]
public class GarbageCollectionCasTests : TestBase
{
    private ApkgDbContext _db = null!;
    private FeatureFoldersProvider _folders = null!;
    private string _objectsRoot = null!;
    private readonly List<string> _createdFiles = [];

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
        _folders = GetService<FeatureFoldersProvider>();
        _objectsRoot = _folders.GetObjectsFolder();
    }

    [TestCleanup]
    public void Cleanup()
    {
        // Remove any leftover CAS files created by tests that passed (GC would normally do this).
        foreach (var f in _createdFiles.Where(File.Exists))
        {
            File.Delete(f);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a dummy 1-byte .deb file to ObjectsRoot using the given sha256 as the filename.
    /// Returns the full path of the created file.
    /// </summary>
    private string CreateCasFile(string sha256)
    {
        var dir = Path.Combine(_objectsRoot, sha256[..2]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sha256}.deb");
        File.WriteAllBytes(path, [0xDE, 0xAD]);
        _createdFiles.Add(path);
        return path;
    }

    /// <summary>
    /// Creates a bucket that is actively referenced (mirror primary) so its
    /// AptPackage rows survive GC phase 1.
    /// </summary>
    private AptBucket CreateActiveBucket()
    {
        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptBuckets.Add(bucket);
        _db.SaveChanges();

        _db.AptMirrors.Add(new AptMirror
        {
            Suite = $"gc-test-{Guid.NewGuid():N}",
            Distro = "ubuntu",
            BaseUrl = "https://example.com/",
            Components = "main",
            Architecture = "amd64",
            PrimaryBucketId = bucket.Id
        });
        _db.SaveChanges();
        return bucket;
    }

    private void AddPackageToBucket(int bucketId, string sha256)
    {
        var pkg = new AptPackage
        {
            BucketId = bucketId,
            Component = "main",
            Architecture = "amd64",
            Package = $"pkg-{sha256[..8]}",
            Version = "1.0",
            Filename = $"pool/main/p/pkg/pkg_{sha256[..8]}_amd64.deb",
            SHA256 = sha256,
            IsVirtual = false,
            OriginSuite = "test",
            OriginComponent = "main",
            Maintainer = "test",
            Description = "test",
            DescriptionMd5 = "test",
            Section = "test",
            Priority = "test",
            Origin = "test",
            Bugs = string.Empty,
            Size = "2",
            MD5sum = string.Empty,
            SHA1 = string.Empty,
            SHA512 = string.Empty
        };
        _db.AptPackages.Add(pkg);
        _db.SaveChanges();
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A .deb file with no corresponding AptPackage or LocalPackage is genuinely
    /// orphaned and must be deleted.
    /// </summary>
    [TestMethod]
    public async Task GcJob_DeletesCasFile_WhenNoPackageReferencesSha256()
    {
        // Arrange: drop a .deb file with a unique SHA256 that nothing in the DB references
        var sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64];
        var path = CreateCasFile(sha256);
        Assert.IsTrue(File.Exists(path), "precondition: file must exist before GC");

        // Act
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert
        Assert.IsFalse(File.Exists(path),
            "GC must delete a .deb file whose SHA256 is not referenced by any AptPackage or LocalPackage.");
        _createdFiles.Remove(path); // already gone, no cleanup needed
    }

    /// <summary>
    /// A .deb file referenced by an active AptPackage must never be deleted.
    /// </summary>
    [TestMethod]
    public async Task GcJob_KeepsCasFile_WhenActiveAptPackageReferencesSha256()
    {
        // Arrange: create a CAS file and an AptPackage in an active (mirror-primary) bucket
        var sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64];
        var path = CreateCasFile(sha256);
        var bucket = CreateActiveBucket();
        AddPackageToBucket(bucket.Id, sha256);

        // Act
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert
        Assert.IsTrue(File.Exists(path),
            "GC must NOT delete a .deb file whose SHA256 is referenced by an active AptPackage.");
    }

    /// <summary>
    /// A .deb file referenced by a LocalPackage (but with no current AptPackage row)
    /// must be preserved. LocalPackages hold a permanent reference to their binaries
    /// so the file survives until the LocalPackage is removed or re-synced.
    /// </summary>
    [TestMethod]
    public async Task GcJob_KeepsCasFile_WhenLocalPackageReferencesSha256()
    {
        // Arrange: CAS file exists, only referenced by a LocalPackage (no AptPackage row yet)
        var userManager = GetService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        var repo = await _db.AptRepositories.FirstAsync();

        var sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64];
        var path = CreateCasFile(sha256);

        _db.LocalPackages.Add(new LocalPackage
        {
            UploadedByUserId = admin!.Id,
            RepositoryId = repo.Id,
            Component = "main",
            Package = $"local-{sha256[..8]}",
            Version = "1.0",
            Architecture = "amd64",
            Maintainer = "test",
            Filename = $"pool/main/l/local/local_{sha256[..8]}_amd64.deb",
            Size = "2",
            SHA256 = sha256,
            IsEnabled = true
        });
        _db.SaveChanges();

        // Act: GC runs — no AptPackage row references this SHA256, only LocalPackage does
        var gc = GetService<GarbageCollectionJob>();
        await gc.ExecuteAsync();

        // Assert: the LocalPackage's binary must be preserved
        Assert.IsTrue(File.Exists(path),
            "GC must NOT delete a .deb file whose SHA256 is referenced by a LocalPackage, " +
            "even if no AptPackage row currently holds that SHA256.");
    }
}
