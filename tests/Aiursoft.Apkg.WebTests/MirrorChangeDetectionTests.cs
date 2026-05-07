using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Security.Cryptography;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class MirrorChangeDetectionTests
{
    private class ChangeableFakeHttpMessageHandler : HttpMessageHandler
    {
        public string Content { get; set; } = "Initial content";
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("Packages"))
            {
                var pkgContent = $@"Package: test-pkg
Architecture: amd64
Version: 1.0.0
Maintainer: test
Description: {Content}
Description-md5: test
Section: test
Priority: test
Size: 100
Filename: pool/main/t/test-pkg/test-pkg_1.0.0_amd64.deb
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(pkgContent)
                });
            }
            if (url.Contains("InRelease") || url.Contains("Release"))
            {
                CallCount++;
                var pkgContent = $@"Package: test-pkg
Architecture: amd64
Version: 1.0.0
Maintainer: test
Description: {Content}
Description-md5: test
Section: test
Priority: test
Size: 100
Filename: pool/main/t/test-pkg/test-pkg_1.0.0_amd64.deb
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
";
                var pkgHash = BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(pkgContent))).Replace("-", "").ToLowerInvariant();

                var response = $@"Codename: focal
Date: {DateTime.UtcNow}
Content: {Content}
SHA256:
 {pkgHash} {Encoding.UTF8.GetByteCount(pkgContent)} main/binary-amd64/Packages
";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(response)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    [TestMethod]
    public async Task TestMirrorSkipsWhenHashMatches()
    {
        var dbName = $@"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        var handler = new ChangeableFakeHttpMessageHandler();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTransient<MirrorSyncJob>();
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
        await db.Database.EnsureCreatedAsync();

        var mirror = new AptMirror
        {
            BaseUrl = "http://upstream.mirror/",
            Distro = "ubuntu",
            Suite = "focal",
            Components = "main",
            Architecture = "amd64",
            AllowInsecure = true
        };
        db.AptMirrors.Add(mirror);
        await db.SaveChangesAsync();

        var mirrorJob = scope.ServiceProvider.GetRequiredService<MirrorSyncJob>();

        // First run - should sync
        await mirrorJob.ExecuteAsync();
        var firstBucketId = (await db.AptMirrors.AsNoTracking().FirstAsync(m => m.Id == mirror.Id)).PrimaryBucketId;
        var firstCallCount = handler.CallCount;
        Assert.IsNotNull(firstBucketId);

        // Second run - same content, should skip
        await mirrorJob.ExecuteAsync();
        var secondMirror = await db.AptMirrors.AsNoTracking().FirstAsync(m => m.Id == mirror.Id);
        Assert.AreEqual(firstBucketId, secondMirror.PrimaryBucketId, "Bucket should not have changed");
        Assert.AreEqual(firstCallCount + 1, handler.CallCount, "Should have called InRelease again to check hash");
        Assert.IsTrue(secondMirror.LastPullResult?.Contains("Successfully pulled 0 packages") ?? false, "Result should indicate 0 packages pulled (skipped)");

        // Third run - changed content, should sync
        handler.Content = "Changed content";
        await mirrorJob.ExecuteAsync();
        var thirdMirror = await db.AptMirrors.AsNoTracking().FirstAsync(m => m.Id == mirror.Id);
        Assert.AreNotEqual(firstBucketId, thirdMirror.PrimaryBucketId, "Bucket should have changed after content change");
        Assert.IsTrue(thirdMirror.LastPullResult?.Contains("Successfully pulled 1 packages") ?? false, "Result should indicate 1 package pulled");
    }
}
