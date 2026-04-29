using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Tests for RepositorySyncJob + LocalPackage override semantics:
///   - An enabled LocalPackage replaces ALL upstream entries for the same (Package, Architecture)
///   - A disabled LocalPackage is ignored
///   - LocalPackage metadata is preserved faithfully in the new bucket
///   - Non-conflicting packages from the mirror survive alongside local packages
///   - Multiple distinct local packages all get inserted
/// </summary>
[TestClass]
public class RepositorySyncLocalPackagesTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;
    private AptBucket _mirrorBucket = null!;
    private AptMirror _mirror = null!;
    private string _adminUserId = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();

        var userManager = GetService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        _adminUserId = admin!.Id;

        // Create an isolated mirror with its own bucket so we can control exactly
        // which upstream packages exist without polluting the seeded data.
        _mirrorBucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptBuckets.Add(_mirrorBucket);
        _db.SaveChanges();

        _mirror = new AptMirror
        {
            Suite = $"test-mirror-{Guid.NewGuid():N}",
            Distro = "ubuntu",
            BaseUrl = "https://example.com/",
            Components = "main",
            Architecture = "amd64",
            PrimaryBucketId = _mirrorBucket.Id
        };
        _db.AptMirrors.Add(_mirror);
        _db.SaveChanges();

        _repo = new AptRepository
        {
            Distro = "test",
            Name = $"Test Repo {Guid.NewGuid():N}",
            Suite = $"test-{Guid.NewGuid():N}",
            Components = "main",
            Architecture = "amd64",
            MirrorId = _mirror.Id
        };
        _db.AptRepositories.Add(_repo);
        _db.SaveChanges();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private void AddMirrorPackage(string name, string version = "1.0", string arch = "amd64", string? sha256 = null)
    {
        var pkg = new AptPackage
        {
            BucketId = _mirrorBucket.Id,
            Component = "main",
            Architecture = arch,
            Package = name,
            Version = version,
            Filename = $"pool/main/{name[0]}/{name}/{name}_{version}_{arch}.deb",
            SHA256 = sha256 ?? ("e3b0c44298fc1c149afbf4c8996fb924" + Guid.NewGuid().ToString("N")[..32]),
            IsVirtual = true,
            RemoteUrl = $"http://example.com/{name}.deb",
            OriginSuite = "upstream",
            OriginComponent = "main",
            Maintainer = "Upstream <upstream@example.com>",
            Description = $"Upstream {name}",
            DescriptionMd5 = "abc",
            Section = "utils",
            Priority = "optional",
            Origin = "Ubuntu",
            Bugs = string.Empty,
            Size = "2048",
            InstalledSize = "8192",
            MD5sum = string.Empty,
            SHA1 = string.Empty,
            SHA512 = string.Empty
        };
        _db.AptPackages.Add(pkg);
        _db.SaveChanges();
    }

    /// <summary>
    /// Sets up a current primary bucket for the repo with a single package that has already
    /// been downloaded (IsVirtual = false). Returns the SHA256 used.
    /// </summary>
    private string SetRepoPrimaryWithRealPackage(string name, string version = "1.0", string arch = "amd64")
    {
        var sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64];
        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        _db.AptBuckets.Add(bucket);
        _db.SaveChanges();

        _db.AptPackages.Add(new AptPackage
        {
            BucketId = bucket.Id,
            Component = "main",
            Architecture = arch,
            Package = name,
            Version = version,
            Filename = $"pool/main/{name[0]}/{name}/{name}_{version}_{arch}.deb",
            SHA256 = sha256,
            IsVirtual = false,      // already downloaded — CAS file is on disk
            RemoteUrl = $"http://example.com/{name}.deb",
            OriginSuite = "upstream",
            OriginComponent = "main",
            Maintainer = "Upstream <upstream@example.com>",
            Description = $"Upstream {name}",
            DescriptionMd5 = "abc",
            Section = "utils",
            Priority = "optional",
            Origin = "Ubuntu",
            Bugs = string.Empty,
            Size = "2048",
            InstalledSize = "8192",
            MD5sum = string.Empty,
            SHA1 = string.Empty,
            SHA512 = string.Empty
        });
        _db.SaveChanges();

        _repo.PrimaryBucketId = bucket.Id;
        _db.SaveChanges();
        return sha256;
    }

    private LocalPackage AddLocalPackage(
        string name,
        string version = "99.0",
        string arch = "amd64",
        bool isEnabled = true,
        string component = "main")
    {
        var sha256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64];
        var lp = new LocalPackage
        {
            UploadedByUserId = _adminUserId,
            RepositoryId = _repo.Id,
            Component = component,
            Package = name,
            Version = version,
            Architecture = arch,
            Maintainer = "Local Uploader <local@example.com>",
            Description = $"Local override of {name}",
            Section = "localutils",
            Priority = "optional",
            Filename = $"pool/{component}/{name[0]}/{name}/{name}_{version}_{arch}.deb",
            Size = "4096",
            SHA256 = sha256,
            IsEnabled = isEnabled
        };
        _db.LocalPackages.Add(lp);
        _db.SaveChanges();
        return lp;
    }

    private async Task<AptBucket?> RunSyncAndGetNewBucket()
    {
        var job = GetService<RepositorySyncJob>();
        await job.ExecuteAsync();

        _db.ChangeTracker.Clear();
        var updatedRepo = await _db.AptRepositories
            .Include(r => r.SecondaryBucket)
            .FirstAsync(r => r.Id == _repo.Id);

        return updatedRepo.SecondaryBucket;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Core override semantics
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncJob_EnabledLocalPackage_RemovesUpstreamVersionAndInsertsLocal()
    {
        // Arrange
        AddMirrorPackage("curl", version: "7.88.1");
        AddLocalPackage("curl", version: "99.0");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: exactly one 'curl' entry in the new bucket — the local one
        var curlPkgs = await _db.AptPackages
            .Where(p => p.BucketId == newBucket!.Id && p.Package == "curl")
            .ToListAsync();

        Assert.AreEqual(1, curlPkgs.Count,
            "LocalPackage must replace the upstream package — only one 'curl' entry must exist.");
        Assert.AreEqual("99.0", curlPkgs[0].Version,
            "The surviving entry must be the LocalPackage version, not the upstream one.");
        Assert.AreEqual("LocalPackage", curlPkgs[0].Origin,
            "Origin must be set to 'LocalPackage' for entries sourced from LocalPackage.");
    }

    [TestMethod]
    public async Task SyncJob_DisabledLocalPackage_DoesNotOverrideUpstream()
    {
        // Arrange: mirror has curl 7.88, local has curl 99 but disabled
        AddMirrorPackage("curl", version: "7.88");
        AddLocalPackage("curl", version: "99.0", isEnabled: false);

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: the upstream version survives
        var curlPkgs = await _db.AptPackages
            .Where(p => p.BucketId == newBucket!.Id && p.Package == "curl")
            .ToListAsync();

        Assert.IsTrue(curlPkgs.Any(p => p.Version == "7.88"),
            "Upstream version must survive when the LocalPackage is disabled.");
        Assert.IsFalse(curlPkgs.Any(p => p.Version == "99.0"),
            "Disabled LocalPackage must NOT appear in the new bucket.");
    }

    [TestMethod]
    public async Task SyncJob_LocalPackage_RemovesAllUpstreamVersionsForSamePackageAndArch()
    {
        // Arrange: multiple upstream versions of the same package
        AddMirrorPackage("libssl3", version: "3.0.2", arch: "amd64");
        AddMirrorPackage("libssl3", version: "3.0.8", arch: "amd64");
        AddLocalPackage("libssl3", version: "99.1", arch: "amd64");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: only the local version
        var pkgs = await _db.AptPackages
            .Where(p => p.BucketId == newBucket!.Id && p.Package == "libssl3")
            .ToListAsync();

        Assert.AreEqual(1, pkgs.Count,
            "All upstream versions of (libssl3, amd64) must be replaced by the single LocalPackage entry.");
        Assert.AreEqual("99.1", pkgs[0].Version);
    }

    [TestMethod]
    public async Task SyncJob_LocalPackage_ArchScope_OnlyRemovesMatchingArch()
    {
        // Arrange: upstream has both amd64 and arm64 versions; local only overrides amd64
        AddMirrorPackage("wget", version: "1.21", arch: "amd64");
        AddMirrorPackage("wget", version: "1.21", arch: "arm64");
        AddLocalPackage("wget", version: "99.0", arch: "amd64");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: amd64 is replaced; arm64 is untouched
        var pkgs = await _db.AptPackages
            .Where(p => p.BucketId == newBucket!.Id && p.Package == "wget")
            .ToListAsync();

        var amd64Pkgs = pkgs.Where(p => p.Architecture == "amd64").ToList();
        var arm64Pkgs = pkgs.Where(p => p.Architecture == "arm64").ToList();

        Assert.AreEqual(1, amd64Pkgs.Count, "amd64 override must produce exactly one entry.");
        Assert.AreEqual("99.0", amd64Pkgs[0].Version, "amd64 must show the LocalPackage version.");
        Assert.AreEqual(1, arm64Pkgs.Count, "arm64 entry must survive untouched.");
        Assert.AreEqual("1.21", arm64Pkgs[0].Version, "arm64 version must remain the upstream value.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Metadata fidelity
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncJob_LocalPackage_MetadataIsPreservedFaithfully()
    {
        // Arrange
        var local = AddLocalPackage("vim", version: "9.1");
        local.Description = "Vi IMproved";
        local.Section = "editors";
        local.Homepage = "https://www.vim.org";
        local.Depends = "libc6 (>= 2.17)";
        _db.SaveChanges();

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert
        var inserted = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "vim");

        Assert.IsNotNull(inserted, "vim must be present in the new bucket.");
        Assert.AreEqual("9.1", inserted.Version);
        Assert.AreEqual("Vi IMproved", inserted.Description);
        Assert.AreEqual("editors", inserted.Section);
        Assert.AreEqual("https://www.vim.org", inserted.Homepage);
        Assert.AreEqual("libc6 (>= 2.17)", inserted.Depends);
        Assert.AreEqual(local.SHA256, inserted.SHA256);
        Assert.AreEqual(local.Filename, inserted.Filename);
        Assert.IsFalse(inserted.IsVirtual, "LocalPackage-sourced entries must not be marked as virtual.");
        Assert.IsNull(inserted.RemoteUrl, "LocalPackage-sourced entries must not have a RemoteUrl.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Coexistence of local and upstream packages
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncJob_NonConflictingMirrorPackages_SurviveAlongsideLocalPackages()
    {
        // Arrange: mirror has 'bash' and 'grep'; local overrides only 'bash'
        AddMirrorPackage("bash", version: "5.2");
        AddMirrorPackage("grep", version: "3.8");
        AddLocalPackage("bash", version: "99.0");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert
        var bashPkg = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "bash");
        var grepPkg = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "grep");

        Assert.IsNotNull(bashPkg, "bash must be present (from LocalPackage).");
        Assert.AreEqual("99.0", bashPkg.Version, "bash must be the LocalPackage version.");

        Assert.IsNotNull(grepPkg, "grep must survive from the mirror, as it was not overridden.");
        Assert.AreEqual("3.8", grepPkg.Version, "grep must keep its original upstream version.");
    }

    [TestMethod]
    public async Task SyncJob_MultipleDistinctLocalPackages_AllInserted()
    {
        // Arrange: no upstream; two distinct local packages
        AddLocalPackage("pkg-a", version: "1.0");
        AddLocalPackage("pkg-b", version: "2.0");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert
        var pkgA = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "pkg-a");
        var pkgB = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "pkg-b");

        Assert.IsNotNull(pkgA, "pkg-a must appear in the new bucket.");
        Assert.IsNotNull(pkgB, "pkg-b must appear in the new bucket.");
        Assert.AreEqual("1.0", pkgA.Version);
        Assert.AreEqual("2.0", pkgB.Version);
    }

    [TestMethod]
    public async Task SyncJob_OnlyEnabledLocalPackage_IsInserted_WhenMixedStates()
    {
        // Arrange: two local packages for same (name, arch); one enabled, one disabled
        AddLocalPackage("mixed-pkg", version: "1.0", isEnabled: false);
        AddLocalPackage("mixed-pkg", version: "2.0", isEnabled: true);

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: only the enabled version
        var pkgs = await _db.AptPackages
            .Where(p => p.BucketId == newBucket!.Id && p.Package == "mixed-pkg")
            .ToListAsync();

        Assert.AreEqual(1, pkgs.Count, "Only the enabled LocalPackage must be inserted.");
        Assert.AreEqual("2.0", pkgs[0].Version);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Standalone repo (no mirror) — local packages still work
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncJob_StandaloneRepo_LocalPackagesAreIncluded()
    {
        // Arrange: detach the mirror from the repo to make it standalone
        _repo.MirrorId = null;
        _db.SaveChanges();

        AddLocalPackage("standalone-pkg", version: "3.0");

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert
        var pkg = await _db.AptPackages
            .FirstOrDefaultAsync(p => p.BucketId == newBucket!.Id && p.Package == "standalone-pkg");

        Assert.IsNotNull(pkg, "LocalPackage must be included even in a standalone (mirror-less) repo.");
        Assert.AreEqual("3.0", pkg.Version);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Re-sync IsVirtual preservation
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task SyncJob_AfterResync_PackageWithSameSha256_StaysReal()
    {
        // Arrange: the repo already has a primary bucket where "wget" was downloaded
        // (IsVirtual = false). The mirror re-syncs with the same version and SHA256.
        var sha256 = SetRepoPrimaryWithRealPackage("wget", version: "1.21");
        AddMirrorPackage("wget", version: "1.21", sha256: sha256);   // same SHA256 in new mirror

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: the CAS file is still on disk (same SHA256) — SyncJob must mark it real
        var pkg = await _db.AptPackages
            .FirstAsync(p => p.BucketId == newBucket!.Id && p.Package == "wget");
        Assert.IsFalse(pkg.IsVirtual,
            "A package whose binary is already on disk (unchanged SHA256) must remain IsVirtual=false after re-sync.");
    }

    [TestMethod]
    public async Task SyncJob_AfterResync_PackageWithNewSha256_BecomesVirtual()
    {
        // Arrange: the repo has a primary bucket where "wget" v1.0 was downloaded.
        // The upstream now has a newer v2.0 with a different SHA256 — the new binary
        // has NOT been downloaded yet.
        SetRepoPrimaryWithRealPackage("wget", version: "1.0");           // old, real
        AddMirrorPackage("wget", version: "2.0");                        // new version, different SHA256

        // Act
        var newBucket = await RunSyncAndGetNewBucket();

        // Assert: the new version has a different SHA256 and no CAS file yet — must be virtual
        var pkg = await _db.AptPackages
            .FirstAsync(p => p.BucketId == newBucket!.Id && p.Package == "wget");
        Assert.IsTrue(pkg.IsVirtual,
            "A package with a new SHA256 (updated upstream) must start as IsVirtual=true; the binary has not been downloaded.");
    }
}
