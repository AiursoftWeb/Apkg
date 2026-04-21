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
        private int _callCount;
        public int CallCount => _callCount;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            // Simulate network delay to guarantee concurrent race conditions are exposed
            await Task.Delay(500, cancellationToken);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            };
        }
    }

    [TestMethod]
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
        connection.Open();
        
        services.AddDbContext<TemplateDbContext, SqliteContext>(options =>
            options.UseSqlite(connection));
            
        services.AddSingleton<StorageRootPathProvider>();
        services.AddSingleton<FeatureFoldersProvider>();
        services.AddSingleton<FileLockProvider>();

        // Setup mock HttpClient with 200ms delay and specific byte content
        var fileContent = "binary-data-for-concurrent-deb-test"u8.ToArray();
        var handler = new CountingHttpMessageHandler(fileContent);
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        services.AddTransient<AptMirrorService>();

        var provider = services.BuildServiceProvider();
        
        // 2. Prepare Database
        var db = provider.GetRequiredService<TemplateDbContext>();
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

        // 3. Act: Fire 100 concurrent requests
        var taskCount = 100;
        var tasks = new Task<string?>[taskCount];
        for (int i = 0; i < taskCount; i++)
        {
            // Use Task.Run to ensure they start on thread pool threads simultaneously
            tasks[i] = Task.Run(async () => 
            {
                using var scope = provider.CreateScope();
                var scopedService = scope.ServiceProvider.GetRequiredService<AptMirrorService>();
                return await scopedService.GetLocalPoolPath(pkg.Filename);
            });
        }

        var results = await Task.WhenAll(tasks);

        // 4. Assert
        
        // B. All tasks should return the exact same non-null path
        var firstResult = results[0];
        Assert.IsNotNull(firstResult, "The returned local path should not be null.");
        foreach (var res in results)
        {
            Assert.AreEqual(firstResult, res, "All concurrent requests should yield the same file path.");
        }

        // A. HttpClient should only be called EXACTLY ONCE despite 100 concurrent requests!
        Assert.AreEqual(1, handler.CallCount, "Concurrent lock failed: HttpClient was called multiple times!");

        // C. The file should exist physically and match expected content
        Assert.IsTrue(File.Exists(firstResult), "The physical file must exist on disk.");
        var diskBytes = await File.ReadAllBytesAsync(firstResult);
        CollectionAssert.AreEqual(fileContent, diskBytes, "The written file content does not match.");

        // D. The database should be updated to IsVirtual = false
        // Note: we must use a fresh context to avoid cached entities from before the ExecuteUpdateAsync
        var freshDb = provider.GetRequiredService<TemplateDbContext>();
        var updatedPkg = await freshDb.AptPackages.FindAsync(pkg.Id);
        
        Assert.IsNotNull(updatedPkg);
        Assert.IsFalse(updatedPkg.IsVirtual, "Database state was not updated to IsVirtual=false!");
    }
}
