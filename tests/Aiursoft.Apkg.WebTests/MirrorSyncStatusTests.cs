using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.BackgroundJobs;
using Aiursoft.Apkg.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Security.Cryptography;

namespace Aiursoft.Apkg.WebTests;

[TestClass]
public class MirrorSyncStatusTests
{
    private class SimpleFakeHttpMessageHandler(string content, string hash) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (url.Contains("Packages"))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(content)
                });
            }
            if (url.Contains("InRelease"))
            {
                var response = $"Codename: focal\nSHA256:\n {hash} {Encoding.UTF8.GetByteCount(content)} main/binary-amd64/Packages";
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(response)
                });
            }
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    [TestMethod]
    public async Task TestMirrorSyncStatusUpdateOnSuccess()
    {
        var dbName = $@"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        var upstreamPackages = @"Package: test-pkg
Architecture: amd64
Version: 1.0.0
Maintainer: test
Description: test
Description-md5: test
Section: test
Priority: test
Size: 100
Filename: pool/main/t/test-pkg/test-pkg_1.0.0_amd64.deb
SHA256: e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
";
        var packagesHash = BitConverter.ToString(SHA256.HashData(Encoding.UTF8.GetBytes(upstreamPackages))).Replace("-", "").ToLower();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTransient<MirrorSyncJob>();
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new SimpleFakeHttpMessageHandler(upstreamPackages, packagesHash));

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
        await mirrorJob.ExecuteAsync();

        var updatedMirror = await db.AptMirrors.FirstAsync(m => m.Id == mirror.Id);
        Assert.IsNotNull(updatedMirror.LastPullTime);
        Assert.IsTrue(updatedMirror.LastPullSuccess);
        Assert.AreEqual("Successfully pulled 1 packages.", updatedMirror.LastPullResult);
        Assert.IsNull(updatedMirror.LastPullErrorStack);
    }

    [TestMethod]
    public async Task TestMirrorSyncStatusUpdateOnFailure()
    {
        var dbName = $@"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(dbName);
        connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApkgDbContext, SqliteContext>(options => options.UseSqlite(dbName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTransient<MirrorSyncJob>();

        // HttpClient that always fails
        services.AddHttpClient(Microsoft.Extensions.Options.Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new ActionMessageHandler(_ => throw new Exception("Network error!")));

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
        await mirrorJob.ExecuteAsync();

        var updatedMirror = await db.AptMirrors.FirstAsync(m => m.Id == mirror.Id);
        Assert.IsNotNull(updatedMirror.LastPullTime);
        Assert.IsFalse(updatedMirror.LastPullSuccess ?? true);
        Assert.IsTrue(updatedMirror.LastPullResult?.Contains("Network error!") ?? false);
        Assert.IsNotNull(updatedMirror.LastPullErrorStack);
    }

    private class ActionMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(action(request));
        }
    }
}
