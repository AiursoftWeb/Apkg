using System.Net;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;

namespace Aiursoft.Apkg.Sdk.Tests;

[TestClass]
public class ApkgPushServiceTests
{
    [TestMethod]
    public async Task PreflightAsync_Success_DeserializesResultAndSendsBearerKey()
    {
        HttpRequestMessage? capturedRequest = null;
        var service = CreateService(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "allPresent": true,
                      "targets": [{
                        "suite": "resolute-addon",
                        "architecture": "amd64",
                        "version": "1.0+resolute",
                        "status": "Present",
                        "repositories": []
                      }]
                    }
                    """)
            };
        });

        var result = await service.PreflightAsync(
            CreatePlan(), "https://apkg.example.com/", "secret-key");

        Assert.IsNotNull(result);
        Assert.IsTrue(result.AllPresent);
        Assert.AreEqual(PackagePreflightStatus.Present, result.Targets.Single().Status);
        Assert.AreEqual(
            "https://apkg.example.com/api/packages/preflight",
            capturedRequest!.RequestUri!.ToString());
        Assert.AreEqual("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.AreEqual("secret-key", capturedRequest.Headers.Authorization.Parameter);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.NotFound)]
    [DataRow(HttpStatusCode.MethodNotAllowed)]
    [DataRow(HttpStatusCode.InternalServerError)]
    public async Task PreflightAsync_CompatibilityFailure_ReturnsNull(
        HttpStatusCode statusCode)
    {
        var service = CreateService(_ => new HttpResponseMessage(statusCode));

        var result = await service.PreflightAsync(
            CreatePlan(), "https://apkg.example.com", "secret-key");

        Assert.IsNull(result);
    }

    [TestMethod]
    [DataRow(HttpStatusCode.Unauthorized)]
    [DataRow(HttpStatusCode.Forbidden)]
    public async Task PreflightAsync_AuthenticationFailure_FailsFast(
        HttpStatusCode statusCode)
    {
        var service = CreateService(_ => new HttpResponseMessage(statusCode));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.PreflightAsync(
                CreatePlan(), "https://apkg.example.com", "bad-key"));
    }

    [TestMethod]
    public async Task PushAsync_ManifestV3_QueriesCapabilitiesBeforeUploading()
    {
        var requests = new List<string>();
        var service = CreateService(request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/api/system/capabilities")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "apkgManifestVersions": [2, 3],
                          "features": ["appstream-assets-v1"]
                        }
                        """)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });
        var archive = await CreateArchiveAsync(3);
        try
        {
            await service.PushAsync(archive, "https://apkg.example.com", "secret-key", true);

            CollectionAssert.AreEqual(
                new[] { "/api/system/capabilities", "/api/packages/apkg-upload" },
                requests);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [TestMethod]
    public async Task PushAsync_ManifestV3_LegacyServerFailsBeforeUpload()
    {
        var requestCount = 0;
        var service = CreateService(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var archive = await CreateArchiveAsync(3);
        try
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                service.PushAsync(archive, "https://apkg.example.com", "secret-key", true));

            Assert.AreEqual(1, requestCount);
            StringAssert.Contains(error.Message, "manifest v3 support");
        }
        finally
        {
            File.Delete(archive);
        }
    }

    private static ApkgPushService CreateService(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new StubHttpMessageHandler(responseFactory);
        return new ApkgPushService(new HttpClient(handler));
    }

    private static PackageBuildPlan CreatePlan() => new()
    {
        Name = "sample",
        Distro = "anduinos",
        Component = "main",
        Targets =
        [
            new PackageBuildTarget
            {
                Suite = "resolute-addon",
                Architecture = "amd64",
                Version = "1.0+resolute"
            }
        ]
    };

    private static async Task<string> CreateArchiveAsync(int formatVersion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"apkg-push-{Guid.NewGuid():N}.apkg");
        var manifest = Encoding.UTF8.GetBytes($"""
            <ApkgPackage FormatVersion="{formatVersion}">
              <Name>sample</Name>
              <Distro>anduinos</Distro>
              <Component>main</Component>
              <Entries />
            </ApkgPackage>
            """);
        await using var output = File.Create(path);
        await using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Pax);
        var entry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.xml")
        {
            DataStream = new MemoryStream(manifest)
        };
        await tar.WriteEntryAsync(entry);
        return path;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }
}
