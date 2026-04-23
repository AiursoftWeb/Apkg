using System.Security.Cryptography;
using System.Text;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Sqlite;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests;

public class FakeGpgSigningService : IGpgSigningService
{
    public Task<string> SignClearsignAsync(string content, string privateKey) => Task.FromResult("SIGNED-CONTENT");
    public Task<(string publicKey, string privateKey, string fingerprint)> GenerateKeyPairAsync(string identity) => 
        Task.FromResult(("PUB", "PRIV", "FPR"));
}

public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _packages;
    private readonly string _packagesHash;
    public FakeHttpMessageHandler(string packages, string packagesHash)
    {
        _packages = packages;
        _packagesHash = packagesHash;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";
        string content;
        if (url.EndsWith("Packages"))
        {
            content = _packages;
        }
        else if (url.Contains("InRelease"))
        {
            content = $"Codename: focal\nSHA256:\n {_packagesHash} {Encoding.UTF8.GetByteCount(_packages)} main/binary-amd64/Packages";
        }
        else
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        });
    }
}

[TestClass]
public class ArchAllIntegrationTests
{
    [TestMethod]
    public async Task TestArchAllPackageSyncAndIndexing()
    {
        var dbName = $@"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        // 1. Prepare upstream fake "Packages" content
        var upstreamPackages = @"Package: test-all-pkg
Architecture: all
Version: 1.2.3
Maintainer: Test Maintainer <test@example.com>
Description: A test package with arch all
Description-md5: 1234567890abcdef
Section: utils
Priority: optional
Filename: pool/main/t/test-all-pkg/test-all-pkg_1.2.3_all.deb
Size: 1000
MD5sum: d41d8cd98f00b204e9800998ecf8427e
SHA1: da39a3ee5e6b4b0d3255bfef95601890afd80709
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
SHA512: cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e

Package: test-amd64-pkg
Architecture: amd64
Version: 1.0.0
Maintainer: Test Maintainer <test@example.com>
Description: A test package with arch amd64
Description-md5: abcdef1234567890
Section: utils
Priority: optional
Filename: pool/main/t/test-amd64-pkg/test-amd64-pkg_1.0.0_amd64.deb
Size: 500
MD5sum: d41d8cd98f00b204e9800998ecf8427e
SHA1: da39a3ee5e6b4b0d3255bfef95601890afd80709
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
SHA512: cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e
";
        var packagesHash = BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(upstreamPackages))).Replace("-", "").ToLower();
        
        // 2. Setup services
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        
        var storagePath = Path.Combine(Path.GetTempPath(), "apkg-test-arch-" + Guid.NewGuid());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Storage:Path"] = storagePath }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddSingleton<FileLockProvider>();
        services.AddTransient<AptMetadataService>();
        services.AddSingleton<IGpgSigningService, FakeGpgSigningService>();
        services.AddTransient<MirrorSyncJob>();
        services.AddTransient<RepositorySyncJob>();
        
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new FakeHttpMessageHandler(upstreamPackages, packagesHash));
        
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        // 3. Setup Mirror and Repository metadata in DB
        var mirror = new AptMirror
        {
            BaseUrl = "http://upstream.mirror/",
            Distro = "ubuntu",
            Suite = "focal",
            Components = "main",
            Architecture = "amd64", 
            SignedBy = null // Disable GPG check
        };
        db.AptMirrors.Add(mirror);
        
        var repo = new AptRepository
        {
            Name = "My Repo",
            Distro = "my-distro",
            Suite = "focal",
            Components = "main",
            Architecture = "amd64",
            Mirror = mirror
        };
        db.AptRepositories.Add(repo);
        await db.SaveChangesAsync();

        // 4. Run MirrorSyncJob
        var mirrorJob = scope.ServiceProvider.GetRequiredService<MirrorSyncJob>();
        await mirrorJob.ExecuteAsync();

        // Verify package architecture in DB
        var archAllPkg = await db.AptPackages.FirstOrDefaultAsync(p => p.Package == "test-all-pkg");
        Assert.IsNotNull(archAllPkg, "Arch:all package should be synced to DB");
        Assert.AreEqual("all", archAllPkg.Architecture, "Architecture should be 'all' from metadata!");

        var amd64Pkg = await db.AptPackages.FirstOrDefaultAsync(p => p.Package == "test-amd64-pkg");
        Assert.IsNotNull(amd64Pkg, "Arch:amd64 package should be synced to DB");
        Assert.AreEqual("amd64", amd64Pkg.Architecture, "Architecture should be 'amd64'!");

        // 5. Run RepositorySyncJob
        var repoJob = scope.ServiceProvider.GetRequiredService<RepositorySyncJob>();
        await repoJob.ExecuteAsync();

        // 6. Verify generated index file
        var folders = scope.ServiceProvider.GetRequiredService<FeatureFoldersProvider>();
        var updatedRepo = await db.AptRepositories.Include(r => r.CurrentBucket).FirstAsync(r => r.Id == repo.Id);
        var currentBucketId = updatedRepo.CurrentBucketId;
        Assert.IsNotNull(currentBucketId);
        
        var packagesFilePath = Path.Combine(folders.GetWorkspaceFolder(), "Buckets", currentBucketId.ToString()!, "main/binary-amd64/Packages");
        Assert.IsTrue(File.Exists(packagesFilePath), "Packages file should be generated for amd64");
        
        var packagesContent = await File.ReadAllTextAsync(packagesFilePath);
        Assert.IsTrue(packagesContent.Contains("Package: test-all-pkg"), "The amd64 index should contain the 'all' architecture package!");
        Assert.IsTrue(packagesContent.Contains("Package: test-amd64-pkg"), "The amd64 index should contain the 'amd64' architecture package!");
        
        var gzPath = packagesFilePath + ".gz";
        Assert.IsTrue(File.Exists(gzPath), "Gzipped packages file should be generated!");
    }
}
