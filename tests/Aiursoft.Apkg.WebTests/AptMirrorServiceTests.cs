using System.Security.Cryptography;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sqlite;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class AptMirrorServiceTests
{
    private class CountingHttpMessageHandler(byte[] content) : HttpMessageHandler
    {
        public int CallCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref CallCount);
            // Extremely short delay to prevent thread starvation but still test concurrency
            await Task.Delay(10, cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
        }
    }

    [TestMethod]
    public async Task TestLocalPackageMissingCasFile_ReturnsNull()
    {
        // Covers the scenario where a local package (IsVirtual=false, RemoteUrl=null) was
        // uploaded successfully but its CAS file was later wiped (e.g. /tmp cleaned on reboot).
        // The service must return null gracefully rather than throw.

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Storage:Path"] = Path.Combine(Path.GetTempPath(), "apkg-test-" + Guid.NewGuid())
            }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);

        var dbName = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddSingleton<FileLockProvider>();
        services.AddHttpClient();
        services.AddTransient<AptMirrorService>();

        var provider = services.BuildServiceProvider();
        var db = provider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        var sha256 = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptBuckets.Add(bucket);
        await db.SaveChangesAsync();

        // Simulate a local package: IsVirtual=false, RemoteUrl=null (no upstream to re-fetch from)
        var pkg = new AptPackage
        {
            BucketId = bucket.Id,
            Component = "main",
            Architecture = "amd64",
            IsVirtual = false,
            RemoteUrl = null,
            Filename = "pool/main/m/motrix/motrix_1.0_amd64.deb",
            SHA256 = sha256,
            Package = "motrix",
            Version = "1.0",
            OriginSuite = "questing-community",
            OriginComponent = "main",
            Maintainer = "test",
            Description = "test",
            DescriptionMd5 = "test",
            Section = "test",
            Priority = "optional",
            Origin = "LocalPackage",
            Bugs = string.Empty,
            Size = "12345",
            MD5sum = string.Empty,
            SHA1 = string.Empty,
            SHA512 = string.Empty,
            InstalledSize = "50",
            OriginalMaintainer = string.Empty,
            Homepage = string.Empty,
            Depends = string.Empty,
            Source = string.Empty,
            MultiArch = string.Empty,
            Provides = string.Empty,
            Suggests = string.Empty,
            Recommends = string.Empty,
            Conflicts = string.Empty,
            Breaks = string.Empty,
            Replaces = string.Empty,
            Extras = []
        };
        db.AptPackages.Add(pkg);
        await db.SaveChangesAsync();

        // CAS file intentionally NOT created on disk — simulates post-wipe state

        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AptMirrorService>();
        var result = await service.GetLocalPoolPath(pkg.Filename);

        Assert.IsNull(result, "Should return null when CAS file is missing and no RemoteUrl exists.");
    }

    [TestMethod]
    [Timeout(5000)] // ABSOLUTE LIMIT: If this test takes longer than 5 seconds, MSTest will violently abort it!
    public async Task TestConcurrentVirtualToPhysicalConversion()
    {
        // 1. Setup DI Container
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Storage:Path"] = Path.Combine(Path.GetTempPath(), "apkg-test-" + Guid.NewGuid())
            }!)
            .Build();
        services.AddSingleton<IConfiguration>(config);

        var dbName = $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open(); // Keep DB alive

        services.AddDbContext<ApkgDbContext, SqliteContext>(options =>
            options.UseSqlite(dbName)); // Let EF manage its own connections from the pool

        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddSingleton<FileLockProvider>();

        var fileContent = "binary-data-for-concurrent-deb-test"u8.ToArray();
        var handler = new CountingHttpMessageHandler(fileContent);
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        services.AddTransient<AptMirrorService>();

        var provider = services.BuildServiceProvider();

        // 2. Prepare Database
        var db = provider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        var sha256 = BitConverter.ToString(SHA256.HashData(fileContent)).Replace("-", "").ToLowerInvariant();

        var bucket = new AptBucket { CreatedAt = DateTime.UtcNow };
        db.AptBuckets.Add(bucket);
        await db.SaveChangesAsync();

        var pkg = new AptPackage
        {
            BucketId = bucket.Id,
            Component = "main",
            Architecture = "amd64",
            IsVirtual = true,
            RemoteUrl = "http://example.com/test.deb",
            Filename = "pool/main/test.deb",
            SHA256 = sha256,

            // Required DebianPackage fields
            Package = "test-pkg",
            Version = "1.0",
            OriginSuite = "test-suite",
            OriginComponent = "main",
            Maintainer = "test",
            Description = "test",
            DescriptionMd5 = "test",
            Section = "test",
            Priority = "test",
            Origin = "test",
            Bugs = "test",
            Size = fileContent.Length.ToString(),
            MD5sum = "test",
            SHA1 = "test",
            SHA512 = "test",
            InstalledSize = "100",
            OriginalMaintainer = "test",
            Homepage = "test",
            Depends = "test",
            Source = "test",
            MultiArch = "test",
            Provides = "test",
            Suggests = "test",
            Recommends = "test",
            Conflicts = "test",
            Breaks = "test",
            Replaces = "test",
            Extras = []
        };
        db.AptPackages.Add(pkg);
        await db.SaveChangesAsync();

        // 3. Act: Fire concurrent requests
        // A reduced task count of 10 avoids connection exhaustion but is enough to test SemaphoreSlim queueing
        var taskCount = 10;
        var tasks = new Task<string?>[taskCount];
        for (int i = 0; i < taskCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                using var scope = provider.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<AptMirrorService>();
                return await scopedService.GetLocalPoolPath(pkg.Filename);
            });
        }

        // Failsafe WaitAsync wrapper to gracefully fail instead of silently hanging
        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(3));

        // 4. Assert
        Console.WriteLine($@"Tasks completed. First result path: {results[0]}");
        var firstResult = results[0];
        Assert.IsNotNull(firstResult, "The returned local path should not be null.");

        // DB State Verification
        using var finalScope = provider.CreateScope();
        var freshDb = finalScope.ServiceProvider.GetRequiredService<ApkgDbContext>();

        // Use AsNoTracking to bypass any internal EF caching
        var updatedPkg = await freshDb.AptPackages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pkg.Id);

        Console.WriteLine($@"Verification - Package: {updatedPkg?.Package}, IsVirtual: {updatedPkg?.IsVirtual}, Filename: {updatedPkg?.Filename}");

        Assert.IsNotNull(updatedPkg);
        Assert.IsFalse(updatedPkg.IsVirtual, $"Database state was not updated to IsVirtual=false! Filename in DB: {updatedPkg.Filename}");
    }
}
