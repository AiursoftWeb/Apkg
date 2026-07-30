using System.Net;
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
