using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Aiursoft.AptClient.Tests;

[TestClass]
public class ChecksumTests
{
    private static string Digest(byte[] data, bool sha512) => Convert.ToHexStringLower(
        sha512 ? SHA512.HashData(data) : SHA256.HashData(data));

    private static (AptPackageSource Source, byte[] Deb) Repository(bool sha512, bool corruptIndex = false, bool corruptDeb = false)
    {
        var deb = Encoding.UTF8.GetBytes("original package payload");
        var algorithm = sha512 ? "SHA512" : "SHA256";
        var index = Encoding.UTF8.GetBytes($"""
            Package: sample
            Version: 1.0
            Architecture: amd64
            Maintainer: Example <example@example.org>
            Description: Example package
            Section: admin
            Priority: optional
            Filename: pool/sample.deb
            Size: {deb.Length}
            {algorithm}: {Digest(deb, sha512)}
            """);
        var release = Encoding.UTF8.GetBytes($"""
            Origin: Example
            {algorithm}:
             {Digest(index, sha512)} {index.Length} main/binary-amd64/Packages
            """);
        var files = new Dictionary<string, byte[]>
        {
            ["/dists/example/InRelease"] = release,
            ["/dists/example/main/binary-amd64/Packages"] = corruptIndex ? [..index, 0] : index,
            ["/pool/sample.deb"] = corruptDeb ? [..deb, 0] : deb
        };
        HttpClient Client() => new(new Handler(files));
        var repository = new UpstreamAptSource("https://example.org", "example", null, true, Client);
        return (new AptPackageSource(repository, "main", "amd64", Client), deb);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReadsIndexAndDownloadsPackage(bool sha512)
    {
        var (source, expected) = Repository(sha512);
        var packages = await source.FetchPackagesAsync().ToListAsync();
        Assert.HasCount(1, packages);
        var package = packages[0].Package;
        Assert.AreEqual(sha512 ? "" : Digest(expected, false), package.SHA256);
        Assert.AreEqual(sha512 ? Digest(expected, true) : "", package.SHA512);
        var destination = Path.GetTempFileName();
        try
        {
            await source.DownloadPackageAsync(package, destination);
            CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(destination));
        }
        finally { File.Delete(destination); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectsTamperedIndex(bool sha512)
    {
        var (source, _) = Repository(sha512, corruptIndex: true);
        var error = await Assert.ThrowsAsync<Exception>(async () => await source.FetchPackagesAsync().ToListAsync());
        StringAssert.Contains(error.Message, "Hash mismatch");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RejectsTamperedPackageAndPreservesExistingDestination(bool sha512)
    {
        var (source, _) = Repository(sha512, corruptDeb: true);
        var packages = await source.FetchPackagesAsync().ToListAsync();
        var destination = Path.GetTempFileName();
        await File.WriteAllTextAsync(destination, "previous good download");
        try
        {
            await Assert.ThrowsAsync<System.Security.SecurityException>(
                () => source.DownloadPackageAsync(packages[0].Package, destination));
            Assert.AreEqual("previous good download", await File.ReadAllTextAsync(destination));
            Assert.IsFalse(File.Exists(destination + ".tmp"));
        }
        finally { File.Delete(destination); }
    }

    [TestMethod]
    public void ExistingSha256SelectionIsUnchangedWhenBothAlgorithmsExist()
    {
        var hashes = DebianPackageParser.ParseInRelease("SHA512:\n " + new string('b', 128) +
            " 1 main/Packages\nSHA256:\n " + new string('a', 64) + " 1 main/Packages\n");
        Assert.AreEqual(new string('a', 64), hashes["main/Packages"]);
    }

    [TestMethod]
    public async Task MissingStrongPackageChecksumIsRejected()
    {
        var (source, _) = Repository(true);
        var package = (await source.FetchPackagesAsync().ToListAsync())[0].Package;
        package.SHA512 = "";
        await Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadPackageAsync(package, "unused.deb"));
    }

    private sealed class Handler(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var found = files.TryGetValue(request.RequestUri!.AbsolutePath, out var data);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new ByteArrayContent(data ?? [])
            });
        }
    }
}
