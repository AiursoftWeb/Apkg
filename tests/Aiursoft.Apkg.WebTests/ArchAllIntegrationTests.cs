using System.Security.Cryptography;
using System.Text;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Sqlite;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Aiursoft.Apkg.WebTests;

public class FakeGpgSigningService : IGpgSigningService
{
    public Task<string> SignClearsignAsync(string content, string privateKey) => Task.FromResult("SIGNED-CONTENT");
    public Task<(string publicKey, string privateKey, string fingerprint)> GenerateKeyPairAsync(string identity) => 
        Task.FromResult(("PUB", "PRIV", "FPR"));
}

[TestClass]
public class ArchAllIntegrationTests
{
    private ServiceProvider _provider = null!;
    private string _dbName = null!;
    private Microsoft.Data.Sqlite.SqliteConnection _connection = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        
        _dbName = $@"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        _connection = new Microsoft.Data.Sqlite.SqliteConnection(_dbName);
        _connection.Open();

        services.AddDbContext<TemplateDbContext, SqliteContext>(options =>
            options.UseSqlite(_dbName));

        var storagePath = Path.Combine(Path.GetTempPath(), "apkg-test-arch-" + Guid.NewGuid());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Storage:Path"] = storagePath
            }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);

        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddSingleton<FileLockProvider>();
        services.AddTransient<AptMetadataService>();
        
        // Use Fake GPG Signing
        services.AddSingleton<IGpgSigningService, FakeGpgSigningService>();

        services.AddTransient<MirrorSyncJob>();
        services.AddTransient<RepositorySyncJob>();

        _provider = services.BuildServiceProvider();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Close();
        _provider.Dispose();
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _packages;
        public MockHttpMessageHandler(string packages) => _packages = packages;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            string content;
            if (url.EndsWith("Packages"))
            {
                content = _packages;
            }
            else if (url.Contains("Release"))
            {
                // Fake Release file
                content = "Codename: focal\nArchitectures: amd64 all\nComponents: main\nSHA256:\n e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 0 main/binary-amd64/Packages";
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

    [TestMethod]
    public async Task TestArchAllPackageSyncAndIndexing()
    {
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
";
        
        // 2. Setup a fresh service collection for this test to inject the mock handler
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDbContext<TemplateDbContext, SqliteContext>(options => options.UseSqlite(_dbName));
        
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
        
        var handler = new MockHttpMessageHandler(upstreamPackages);
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        
        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        await db.Database.EnsureCreatedAsync();

        // 3. Setup Mirror and Repository metadata in DB
        var mirror = new AptMirror
        {
            BaseUrl = "http://upstream.mirror/",
            Distro = "ubuntu",
            Suite = "focal",
            Components = "main",
            Architecture = "amd64" // We sync amd64, but expect to find 'all' packages
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

        // Verify package architecture in DB is 'all'
        var pkgInDb = await db.AptPackages.FirstOrDefaultAsync(p => p.Package == "test-all-pkg");
        Assert.IsNotNull(pkgInDb, "Package should be synced to DB");
        Assert.AreEqual("all", pkgInDb.Architecture, "Architecture should be 'all' from metadata, not 'amd64' from loop!");

        // 5. Run RepositorySyncJob
        var repoJob = scope.ServiceProvider.GetRequiredService<RepositorySyncJob>();
        await repoJob.ExecuteAsync();

        // 6. Verify generated index file
        var folders = scope.ServiceProvider.GetRequiredService<FeatureFoldersProvider>();
        var currentBucket = await db.AptBuckets.OrderByDescending(b => b.Id).FirstOrDefaultAsync();
        Assert.IsNotNull(currentBucket);
        
        var packagesFilePath = Path.Combine(folders.GetWorkspaceFolder(), "Buckets", currentBucket.Id.ToString(), "main/binary-amd64/Packages");
        Assert.IsTrue(File.Exists(packagesFilePath), "Packages file should be generated for amd64");
        
        var packagesContent = await File.ReadAllTextAsync(packagesFilePath);
        Assert.IsTrue(packagesContent.Contains("Package: test-all-pkg"), "The amd64 index should contain the 'all' architecture package!");
        Assert.IsTrue(packagesContent.Contains("Architecture: all"), "Metadata should preserve 'all' architecture");
    }
}
