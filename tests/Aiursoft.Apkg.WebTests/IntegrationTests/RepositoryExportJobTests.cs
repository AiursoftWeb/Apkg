using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class RepositoryExportJobTests : TestBase
{
    private ApkgDbContext _db = null!;
    private FeatureFoldersProvider _folders = null!;
    private string _exportRoot = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        _db = GetService<ApkgDbContext>();
        _folders = GetService<FeatureFoldersProvider>();
        _exportRoot = GetService<IConfiguration>()["Storage:ExportPath"]!;

        // Isolate each test from prior tests in the same class: remove all
        // test-created repos and their packages, but keep seed certs.
        _db.AptPackages.RemoveRange(_db.AptPackages);
        _db.AptRepositories.RemoveRange(_db.AptRepositories);
        _db.AptBuckets.RemoveRange(_db.AptBuckets);
        await _db.SaveChangesAsync();
    }

    [TestCleanup]
    public override void CleanTestContext()
    {
        var cleanRoot = _exportRoot.TrimEnd('/');
        foreach (var dir in new[] { cleanRoot, cleanRoot + "_stage", cleanRoot + "_prev" })
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { /* best-effort */ }
        }
        base.CleanTestContext();
    }

    private AptBucket CreateBucket(string? releaseContent = null, string? inReleaseContent = null)
    {
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            ReleaseContent = releaseContent,
            InReleaseContent = inReleaseContent
        };
        _db.AptBuckets.Add(bucket);
        _db.SaveChanges();
        return bucket;
    }

    private static AptPackage CreatePackage(
        int bucketId,
        string package,
        string version,
        string architecture,
        string component,
        string sha256,
        string filename)
    {
        return new AptPackage
        {
            BucketId = bucketId,
            Component = component,
            IsVirtual = false,
            OriginSuite = "noble",
            OriginComponent = component,
            Package = package,
            Version = version,
            Architecture = architecture,
            Maintainer = "Test Maintainer <test@example.com>",
            Description = "Test package for export job tests",
            DescriptionMd5 = "00000000000000000000000000000000",
            Section = "utils",
            Priority = "optional",
            Origin = "test",
            Bugs = "https://example.com/bugs",
            Filename = filename,
            Size = "1024",
            MD5sum = "00000000000000000000000000000000",
            SHA1 = "0000000000000000000000000000000000000000",
            SHA256 = sha256,
            SHA512 = "00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
        };
    }

    private static AptRepository CreateRepo(string distro, string name, string suite,
        string components, string architecture, int? primaryBucketId)
    {
        return new AptRepository
        {
            Distro = distro,
            Name = name,
            Suite = suite,
            Components = components,
            Architecture = architecture,
            PrimaryBucketId = primaryBucketId
        };
    }

    // ──────────────────────────────────────────────────────────────
    // Basic contract tests
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_NoRepositories_DoesNotCreateDistroDirs()
    {
        // Remove all repos and certs — nothing to export.
        _db.AptCertificates.RemoveRange(_db.AptCertificates);
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        Assert.IsFalse(Directory.Exists(Path.Combine(liveDir, "artifacts")),
            "No artifacts directory at all when repos and certs are both empty.");
    }

    [TestMethod]
    public async Task Export_RepositoryWithoutPrimaryBucket_IsSkipped()
    {
        _db.AptRepositories.Add(CreateRepo("testos", "no-bucket", "noble", "main", "amd64", null));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var distsDir = Path.Combine(liveDir, "artifacts", "testos", "dists");
        Assert.IsFalse(Directory.Exists(distsDir));
    }

    // ──────────────────────────────────────────────────────────────
    // Dists metadata export
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_WithPrimaryBucket_WritesInReleaseAndRelease()
    {
        const string releaseContent = "Origin: Apkg\nSuite: noble\nArchitectures: amd64\nComponents: main\nSHA256:\n";
        const string inReleaseContent = "-----BEGIN PGP SIGNED MESSAGE-----\nHash: SHA256\n\n" + releaseContent + "\n-----BEGIN PGP SIGNATURE-----\nABC123\n-----END PGP SIGNATURE-----\n";

        var bucket = CreateBucket(releaseContent, inReleaseContent);
        _db.AptRepositories.Add(CreateRepo("testos", "test1", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var distsDir = Path.Combine(liveDir, "artifacts", "testos", "dists", "noble");

        Assert.IsTrue(File.Exists(Path.Combine(distsDir, "InRelease")));
        Assert.AreEqual(inReleaseContent, await File.ReadAllTextAsync(Path.Combine(distsDir, "InRelease")));

        Assert.IsTrue(File.Exists(Path.Combine(distsDir, "Release")));
        Assert.AreEqual(releaseContent, await File.ReadAllTextAsync(Path.Combine(distsDir, "Release")));
    }

    [TestMethod]
    public async Task Export_BucketWithoutInRelease_WritesOnlyRelease()
    {
        const string releaseContent = "Origin: Apkg\nSuite: noble\n";
        var bucket = CreateBucket(releaseContent, inReleaseContent: null);
        _db.AptRepositories.Add(CreateRepo("testos", "nosig", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var distsDir = Path.Combine(liveDir, "artifacts", "testos", "dists", "noble");

        Assert.IsTrue(File.Exists(Path.Combine(distsDir, "Release")));
        Assert.IsFalse(File.Exists(Path.Combine(distsDir, "InRelease")),
            "InRelease should not be written when bucket has no signed content.");
    }

    // ──────────────────────────────────────────────────────────────
    // Packages / Contents file copy from bucket
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_CopiesPackagesAndContentsFromBucket()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\nArchitectures: amd64\nComponents: main\nSHA256:\n");

        var bucketDir = Path.Combine(_folders.GetBucketsFolder(), bucket.Id.ToString());
        var pkgDir = Path.Combine(bucketDir, "main", "binary-amd64");
        Directory.CreateDirectory(pkgDir);
        var packagesContent = "Package: test-pkg\nVersion: 1.0\nArchitecture: amd64\nFilename: noble/pool/main/t/test-pkg/test-pkg_1.0_amd64.deb\n\n";
        await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages"), packagesContent);
        await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages.gz"), "gzipped");

        var contentsDir = Path.Combine(bucketDir, "main");
        var contentsContent = "usr/bin/test-pkg\ttest-pkg\n";
        await File.WriteAllTextAsync(Path.Combine(contentsDir, "Contents-amd64"), contentsContent);
        await File.WriteAllTextAsync(Path.Combine(contentsDir, "Contents-amd64.gz"), "contents-gzipped");

        _db.AptRepositories.Add(CreateRepo("testos", "test2", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var basePath = Path.Combine(liveDir, "artifacts", "testos", "dists", "noble");

        Assert.IsTrue(File.Exists(Path.Combine(basePath, "main/binary-amd64/Packages")));
        Assert.AreEqual(packagesContent, await File.ReadAllTextAsync(Path.Combine(basePath, "main/binary-amd64/Packages")));

        Assert.IsTrue(File.Exists(Path.Combine(basePath, "main/binary-amd64/Packages.gz")));
        Assert.AreEqual("gzipped", await File.ReadAllTextAsync(Path.Combine(basePath, "main/binary-amd64/Packages.gz")));

        Assert.IsTrue(File.Exists(Path.Combine(basePath, "main/Contents-amd64")));
        Assert.AreEqual(contentsContent, await File.ReadAllTextAsync(Path.Combine(basePath, "main/Contents-amd64")));

        Assert.IsTrue(File.Exists(Path.Combine(basePath, "main/Contents-amd64.gz")));
        Assert.AreEqual("contents-gzipped", await File.ReadAllTextAsync(Path.Combine(basePath, "main/Contents-amd64.gz")));
    }

    // ──────────────────────────────────────────────────────────────
    // Certificate export
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_Certificates_ExportedToCertsDirectory()
    {
        // Ensure at least one cert exists. Seed data provides one, but a prior test
        // (Export_NoRepositories) may have removed it due to alphabetic ordering.
        if (!await _db.AptCertificates.AnyAsync())
        {
            _db.AptCertificates.Add(new AptCertificate
            {
                Name = "test-cert",
                FriendlyName = "Test Certificate",
                PublicKey = "-----BEGIN PGP PUBLIC KEY BLOCK-----\ntest\n-----END PGP PUBLIC KEY BLOCK-----",
                PrivateKey = "-----BEGIN PGP PRIVATE KEY BLOCK-----\ntest\n-----END PGP PRIVATE KEY BLOCK-----",
                Fingerprint = "0123456789ABCDEF"
            });
            await _db.SaveChangesAsync();
        }

        var certs = await _db.AptCertificates.AsNoTracking().ToListAsync();
        Assert.IsTrue(certs.Count > 0, "Pre-condition: at least one certificate must exist.");

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var certsDir = Path.Combine(liveDir, "artifacts", "certs");

        foreach (var cert in certs)
        {
            var certPath = Path.Combine(certsDir, cert.Name);
            Assert.IsTrue(File.Exists(certPath),
                $"Certificate at artifacts/certs/{cert.Name} should exist.");
            Assert.AreEqual(cert.PublicKey, await File.ReadAllTextAsync(certPath));
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Pool hardlinks
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_PoolFiles_CreatesHardlinksAtCorrectPaths()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\n");

        var objectsRoot = _folders.GetObjectsFolder();
        var sha256 = "a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6a7b8c9d0e1f2a3b4c5d6a7b8c9d0ab12";
        var casDir = Path.Combine(objectsRoot, sha256[..2]);
        Directory.CreateDirectory(casDir);
        await File.WriteAllTextAsync(Path.Combine(casDir, $"{sha256}.deb"), "fake-deb-content");

        var pkg = CreatePackage(bucket.Id, "test-pkg", "1.0", "amd64", "main", sha256,
            "pool/main/t/test-pkg/test-pkg_1.0_amd64.deb");
        _db.AptPackages.Add(pkg);

        _db.AptRepositories.Add(CreateRepo("testos", "pool1", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');

        // Suite-scoped
        var suitePath = Path.Combine(liveDir, "artifacts", "testos",
            "noble/pool/main/t/test-pkg/test-pkg_1.0_amd64.deb");
        Assert.IsTrue(File.Exists(suitePath));
        Assert.AreEqual("fake-deb-content", await File.ReadAllTextAsync(suitePath));

        // Distro-scoped (without suite prefix)
        var distroPath = Path.Combine(liveDir, "artifacts", "testos",
            "pool/main/t/test-pkg/test-pkg_1.0_amd64.deb");
        Assert.IsTrue(File.Exists(distroPath));
        Assert.AreEqual("fake-deb-content", await File.ReadAllTextAsync(distroPath));
    }

    [TestMethod]
    public async Task Export_PoolFiles_CasFileMissing_SkipsPackage()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\n");
        var sha256 = "deadbeef0000000000000000000000000000000000000000000000000000dead";

        _db.AptPackages.Add(CreatePackage(bucket.Id, "missing-pkg", "1.0", "amd64", "main", sha256,
            "pool/main/m/missing-pkg/missing-pkg_1.0_amd64.deb"));
        _db.AptRepositories.Add(CreateRepo("testos", "pool2", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var poolPath = Path.Combine(liveDir, "artifacts", "testos",
            "noble/pool/main/m/missing-pkg/missing-pkg_1.0_amd64.deb");
        Assert.IsFalse(File.Exists(poolPath));
    }

    // ──────────────────────────────────────────────────────────────
    // Multi-arch / multi-component
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_MultiArchMultiComponent_WritesAllCombinations()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\n");

        var bucketDir = Path.Combine(_folders.GetBucketsFolder(), bucket.Id.ToString());

        foreach (var arch in new[] { "amd64", "arm64" })
        foreach (var component in new[] { "main", "contrib" })
        {
            var pkgDir = Path.Combine(bucketDir, component, $"binary-{arch}");
            Directory.CreateDirectory(pkgDir);
            await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages"), $"packages-{component}-{arch}");
            await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages.gz"), $"packages-gz-{component}-{arch}");

            var cDir = Path.Combine(bucketDir, component);
            await File.WriteAllTextAsync(Path.Combine(cDir, $"Contents-{arch}"), $"contents-{component}-{arch}");
            await File.WriteAllTextAsync(Path.Combine(cDir, $"Contents-{arch}.gz"), $"contents-gz-{component}-{arch}");
        }

        _db.AptRepositories.Add(CreateRepo("testos", "multi", "noble", "main,contrib", "amd64,arm64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var distsDir = Path.Combine(liveDir, "artifacts", "testos", "dists", "noble");

        foreach (var arch in new[] { "amd64", "arm64" })
        foreach (var component in new[] { "main", "contrib" })
        {
            var pkgPath = Path.Combine(distsDir, component, $"binary-{arch}", "Packages");
            Assert.IsTrue(File.Exists(pkgPath), $"Missing: {pkgPath}");
            Assert.AreEqual($"packages-{component}-{arch}", await File.ReadAllTextAsync(pkgPath));

            Assert.IsTrue(File.Exists(Path.Combine(distsDir, component, $"binary-{arch}", "Packages.gz")));
            Assert.IsTrue(File.Exists(Path.Combine(distsDir, component, $"Contents-{arch}")));
            Assert.IsTrue(File.Exists(Path.Combine(distsDir, component, $"Contents-{arch}.gz")));
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Atomic swap
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_SuccessfulRun_PerformsAtomicSwap()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\n");
        _db.AptRepositories.Add(CreateRepo("testos", "swap", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var cleanRoot = _exportRoot.TrimEnd('/');

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        Assert.IsTrue(Directory.Exists(cleanRoot), "Live export directory should exist.");
        Assert.IsFalse(Directory.Exists(cleanRoot + "_stage"), "Stage should be cleaned up after swap.");
    }

    [TestMethod]
    public async Task Export_TwoConsecutiveRuns_PreservesLatestContentAndMovesOldToPrev()
    {
        var bucket1 = CreateBucket("Origin: Apkg\nSuite: noble\nRun: 1\n");
        _db.AptRepositories.Add(CreateRepo("testos", "twice", "noble", "main", "amd64", bucket1.Id));
        await _db.SaveChangesAsync();

        var cleanRoot = _exportRoot.TrimEnd('/');
        var job = GetService<RepositoryExportJob>();

        await job.ExecuteAsync();
        var releasePath = Path.Combine(cleanRoot, "artifacts", "testos", "dists", "noble", "Release");
        Assert.IsTrue(File.Exists(releasePath));
        Assert.IsTrue((await File.ReadAllTextAsync(releasePath)).Contains("Run: 1"));

        // Second run with updated content
        _db.ChangeTracker.Clear();
        var existingBucket = await _db.AptBuckets.FindAsync(bucket1.Id);
        existingBucket!.ReleaseContent = "Origin: Apkg\nSuite: noble\nRun: 2\n";
        await _db.SaveChangesAsync();

        await job.ExecuteAsync();

        Assert.IsTrue((await File.ReadAllTextAsync(releasePath)).Contains("Run: 2"));

        var prevDir = cleanRoot + "_prev";
        Assert.IsTrue(Directory.Exists(prevDir));
        var prevReleasePath = Path.Combine(prevDir, "artifacts", "testos", "dists", "noble", "Release");
        Assert.IsTrue(File.Exists(prevReleasePath));
        Assert.IsTrue((await File.ReadAllTextAsync(prevReleasePath)).Contains("Run: 1"));
    }

    // ──────────────────────────────────────────────────────────────
    // URL alignment with controller routes
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_DirectoryStructure_MatchesControllerRoutes()
    {
        var objectsRoot = _folders.GetObjectsFolder();
        var sha256 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        var casDir = Path.Combine(objectsRoot, sha256[..2]);
        Directory.CreateDirectory(casDir);
        await File.WriteAllTextAsync(Path.Combine(casDir, $"{sha256}.deb"), "content");

        var bucket = CreateBucket(
            "Origin: Apkg\nSuite: noble\nArchitectures: amd64\nComponents: main\nSHA256:\n",
            "-----BEGIN PGP SIGNED MESSAGE-----\nHash: SHA256\n\nOrigin: Apkg\nSuite: noble\n\n-----BEGIN PGP SIGNATURE-----\nsig\n-----END PGP SIGNATURE-----");

        var bucketDir = Path.Combine(_folders.GetBucketsFolder(), bucket.Id.ToString());
        var pkgDir = Path.Combine(bucketDir, "main", "binary-amd64");
        Directory.CreateDirectory(pkgDir);
        await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages"),
            "Package: test\nVersion: 1.0\nArchitecture: amd64\nFilename: noble/pool/main/t/test/test_1.0_amd64.deb\n\n");
        await File.WriteAllTextAsync(Path.Combine(pkgDir, "Packages.gz"), "gz");

        var contentsDir = Path.Combine(bucketDir, "main");
        await File.WriteAllTextAsync(Path.Combine(contentsDir, "Contents-amd64"), "usr/bin/test\ttest\n");
        await File.WriteAllTextAsync(Path.Combine(contentsDir, "Contents-amd64.gz"), "gz");

        _db.AptPackages.Add(CreatePackage(bucket.Id, "test", "1.0", "amd64", "main", sha256,
            "pool/main/t/test/test_1.0_amd64.deb"));
        _db.AptRepositories.Add(CreateRepo("testos", "route", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        var artifacts = Path.Combine(liveDir, "artifacts");

        // Controller: GET artifacts/{distro}/dists/{suite}/{**path}
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/InRelease")));
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/Release")));
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/main/binary-amd64/Packages")));
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/main/binary-amd64/Packages.gz")));
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/main/Contents-amd64")));
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/dists/noble/main/Contents-amd64.gz")));

        // Controller: GET artifacts/{distro}/{suite}/pool/{**path} (GetSuitePool)
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/noble/pool/main/t/test/test_1.0_amd64.deb")));

        // Controller: GET artifacts/{distro}/pool/{**path} (GetPool, distro-scoped fallback)
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "testos/pool/main/t/test/test_1.0_amd64.deb")));

        // Controller: GET artifacts/certs/{name}
        var cert = await _db.AptCertificates.FirstOrDefaultAsync();
        if (cert == null)
        {
            cert = new AptCertificate
            {
                Name = "route-cert",
                FriendlyName = "Test",
                PublicKey = "key",
                PrivateKey = "priv",
                Fingerprint = "ABCDEF"
            };
            _db.AptCertificates.Add(cert);
            await _db.SaveChangesAsync();
        }
        Assert.IsTrue(File.Exists(Path.Combine(artifacts, "certs", cert.Name)));
    }

    // ──────────────────────────────────────────────────────────────
    // Post-run cleanup
    // ──────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Export_AfterSuccessfulRun_StageIsCleanedUp()
    {
        var bucket = CreateBucket("Origin: Apkg\nSuite: noble\n");
        _db.AptRepositories.Add(CreateRepo("testos", "cleanup", "noble", "main", "amd64", bucket.Id));
        await _db.SaveChangesAsync();

        var cleanRoot = _exportRoot.TrimEnd('/');

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        Assert.IsFalse(Directory.Exists(cleanRoot + "_stage"));
    }

    [TestMethod]
    public async Task Export_AllReposSkipped_DoesNotCreateDistroDirs()
    {
        // Repo without PrimaryBucket — skipped
        _db.AptRepositories.Add(CreateRepo("testos", "skipped", "noble", "main", "amd64", null));
        await _db.SaveChangesAsync();

        var job = GetService<RepositoryExportJob>();
        await job.ExecuteAsync();

        var liveDir = _exportRoot.TrimEnd('/');
        Assert.IsFalse(Directory.Exists(Path.Combine(liveDir, "artifacts", "testos")));
    }
}
