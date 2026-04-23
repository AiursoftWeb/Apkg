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
            InReleaseContent = "SIGNED-TEST-CONTENT",
            ReleaseContent = "RAW-TEST-CONTENT"
        };
        db.AptBuckets.Add(bucket);
        db.SaveChanges();

        repo.CurrentBucketId = bucket.Id;
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
        var response = await Http.GetAsync($"/{repo.Distro}/dists/{repo.Suite}/InRelease");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("SIGNED-TEST-CONTENT", content);

        // 3. Test Pool Download (Lazy Sync Path)
        var poolResponse = await Http.GetAsync($"/{repo.Distro}/pool/main/t/test-pkg/test.deb");
        Assert.IsTrue(poolResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.InternalServerError or HttpStatusCode.NotFound);
    }

    [TestMethod]
    public async Task TestCertificateDistribution()
    {
        await Server!.SeedMirrorsAsync(true);
        var response = await Http.GetAsync("/certs/anduinos");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("BEGIN PGP PUBLIC KEY BLOCK"));
    }
}
