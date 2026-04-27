using System.Net;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

[TestClass]
public class MirrorsIndexTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptMirror _mirror = null!;
    private AptBucket _bucket = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _mirror = _db.AptMirrors.First();

        _bucket = new AptBucket
        {
            CreatedAt = DateTime.UtcNow
        };
        _db.AptBuckets.Add(_bucket);
        _db.SaveChanges();

        _mirror.PrimaryBucketId = _bucket.Id;
        _mirror.LastPullTime = DateTime.UtcNow;
        _mirror.LastPullSuccess = true;
        _mirror.LastPullResult = "Fetched metadata.";
        _db.AptPackages.Add(new AptPackage
        {
            BucketId = _bucket.Id,
            Component = "main",
            Architecture = "amd64",
            Package = "demo-package",
            Version = "1.0.0",
            Filename = "pool/main/d/demo-package/demo-package_1.0.0_amd64.deb",
            SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            IsVirtual = true,
            RemoteUrl = "http://example.com/demo-package.deb",
            OriginSuite = _mirror.Suite,
            OriginComponent = "main",
            Maintainer = "Mirror Maintainer <maint@example.com>",
            Description = "Mirror test package",
            DescriptionMd5 = "abc",
            Section = "utils",
            Priority = "optional",
            Origin = "Ubuntu",
            Bugs = "https://bugs.example.com",
            Size = "2048",
            MD5sum = "abc",
            SHA1 = "abc",
            SHA512 = "abc"
        });
        _db.SaveChanges();
    }

    [TestMethod]
    public async Task MirrorsIndex_ShowsDedicatedLastPullColumn_WithoutShiftingBucketAndPackageColumns()
    {
        var response = await Http.GetAsync("/Mirrors");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Last Pull"), "The table should include a dedicated Last Pull header.");
        Assert.IsTrue(html.Contains($"/Buckets/Packages/{_bucket.Id}"), "Primary bucket should link to the bucket packages page.");
        Assert.IsTrue(html.Contains($"/Mirrors/Packages/{_mirror.Id}"), "Packages should link to the mirror packages page.");
        Assert.IsTrue(html.Contains("1 items"), "Packages column should show the package count instead of a bucket id.");

        var lastPullHeaderIndex = html.IndexOf("Last Pull", StringComparison.Ordinal);
        var primaryBucketHeaderIndex = html.IndexOf("Primary Bucket", StringComparison.Ordinal);
        var packagesHeaderIndex = html.IndexOf(">Packages<", StringComparison.Ordinal);
        var primaryBucketLinkIndex = html.IndexOf($"/Buckets/Packages/{_bucket.Id}", StringComparison.Ordinal);
        var packagesLinkIndex = html.IndexOf($"/Mirrors/Packages/{_mirror.Id}", StringComparison.Ordinal);

        Assert.IsTrue(lastPullHeaderIndex >= 0 && primaryBucketHeaderIndex > lastPullHeaderIndex,
            "Primary Bucket header should appear after Last Pull.");
        Assert.IsTrue(packagesHeaderIndex > primaryBucketHeaderIndex,
            "Packages header should appear after Primary Bucket.");
        Assert.IsTrue(primaryBucketLinkIndex >= 0 && packagesLinkIndex > primaryBucketLinkIndex,
            "Packages link should appear after the primary bucket link in the row.");
    }
}
