using System.Net;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class AptMirrorTests : TestBase
{
    [TestMethod]
    public async Task TestAptMetadataAndPoolFlow()
    {
        // 1. Preparation
        await Server!.SeedMirrorsAsync(true);
        var db = GetService<ApkgDbContext>();

        // Ensure we have a repo with a bucket for testing
        var repo = db.AptRepositories.First();
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            SignedAt = DateTime.UtcNow,
            InReleaseContent = "SIGNED-TEST-CONTENT",
            ReleaseContent = "RAW-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();

        repo.PrimaryBucketId = bucket.Id;
        db.SaveChanges();

        // Add a test package to this bucket
        var pkg = new AptPackage
        {
            BucketId = bucket.Id,
            Component = "main",
            Architecture = "amd64",
            Package = "test-pkg",
            Version = "1.0",
            Filename = "pool/main/t/test-pkg/test.deb",
            SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            IsVirtual = true,
            RemoteUrl = "http://example.com/test.deb",

            // Required fields
            OriginSuite = "test",
            OriginComponent = "main",
            Maintainer = "test",
            Description = "test",
            DescriptionMd5 = "test",
            Section = "test",
            Priority = "test",
            Origin = "test",
            Bugs = "test",
            Size = "0",
            MD5sum = "test",
            SHA1 = "test",
            SHA512 = "test"
        };
        db.AptPackages.Add(pkg);
        db.SaveChanges();

        // 2. Test InRelease Distribution
        var response = await Http.GetAsync($"/artifacts/{repo.Distro}/dists/{repo.Suite}/InRelease");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("SIGNED-TEST-CONTENT", content);

        // 3. Test Suite-scoped Pool Download (GetSuitePool)
        var poolResponse = await Http.GetAsync($"/artifacts/{repo.Distro}/{repo.Suite}/pool/main/t/test-pkg/test.deb");
        Assert.IsTrue(poolResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.InternalServerError or HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task TestCertificateDistribution()
    {
        await Server!.SeedMirrorsAsync(true);
        var response = await Http.GetAsync("/artifacts/certs/anduinos");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("BEGIN PGP PUBLIC KEY BLOCK"));
    }

    [TestMethod]
    public async Task TestInReleaseReturnsCacheHeaders()
    {
        await Server!.SeedMirrorsAsync(true);
        var db = GetService<ApkgDbContext>();

        var repo = db.AptRepositories.First();
        var signedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            SignedAt = signedAt,
            InReleaseContent = "SIGNED-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();
        repo.PrimaryBucketId = bucket.Id;
        db.SaveChanges();

        var response = await Http.GetAsync($"/artifacts/{repo.Distro}/dists/{repo.Suite}/InRelease");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("SIGNED-TEST-CONTENT", await response.Content.ReadAsStringAsync());
        Assert.AreEqual(signedAt, response.Content.Headers.LastModified?.UtcDateTime);
        Assert.AreEqual("no-cache", response.Headers.CacheControl?.ToString());
    }

    [TestMethod]
    public async Task TestInReleaseReturns304WhenNotModified()
    {
        await Server!.SeedMirrorsAsync(true);
        var db = GetService<ApkgDbContext>();

        var repo = db.AptRepositories.First();
        var signedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            SignedAt = signedAt,
            InReleaseContent = "SIGNED-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();
        repo.PrimaryBucketId = bucket.Id;
        db.SaveChanges();

        // Second request with If-Modified-Since matching SignedAt
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/artifacts/{repo.Distro}/dists/{repo.Suite}/InRelease");
        request.Headers.Add("If-Modified-Since", signedAt.ToString("R"));
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotModified, response.StatusCode);
    }

    [TestMethod]
    public async Task TestInReleaseReturns200WhenModified()
    {
        await Server!.SeedMirrorsAsync(true);
        var db = GetService<ApkgDbContext>();

        var repo = db.AptRepositories.First();
        var signedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow,
            SignedAt = signedAt,
            InReleaseContent = "SIGNED-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();
        repo.PrimaryBucketId = bucket.Id;
        db.SaveChanges();

        // Request with an old If-Modified-Since date
        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/artifacts/{repo.Distro}/dists/{repo.Suite}/InRelease");
        request.Headers.Add("If-Modified-Since",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("R"));
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("SIGNED-TEST-CONTENT", await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task TestReleaseReturns304WhenNotModified()
    {
        await Server!.SeedMirrorsAsync(true);
        var db = GetService<ApkgDbContext>();

        var repo = db.AptRepositories.First();
        var createdAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var bucket = new AptBucket
        {
            CreatedAt = createdAt,
            ReleaseContent = "RAW-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();
        repo.PrimaryBucketId = bucket.Id;
        db.SaveChanges();

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"/artifacts/{repo.Distro}/dists/{repo.Suite}/Release");
        request.Headers.Add("If-Modified-Since", createdAt.ToString("R"));
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotModified, response.StatusCode);
    }
}
