using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

public abstract class TestBase
{
    // Per-derived-class host storage. Each test class gets its own isolated
    // IHost from the factory, so classes can run in parallel.
    private static readonly ConcurrentDictionary<string, IHost> Hosts = new();
    private static readonly ConcurrentDictionary<string, int> Ports = new();

    protected HttpClient Http = null!;
    protected IHost? Server => Hosts.TryGetValue(GetType().FullName!, out var host) ? host : null;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassSetup(TestContext context)
    {
        var (host, port) = await TestAssemblySetup.CreateIsolatedHostAsync();
        Hosts[context.FullyQualifiedTestClassName] = host;
        Ports[context.FullyQualifiedTestClassName] = port;
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassTeardown(TestContext context)
    {
        if (Hosts.TryRemove(context.FullyQualifiedTestClassName, out var host))
        {
            await host.StopAsync();
            host.Dispose();
        }
        Ports.TryRemove(context.FullyQualifiedTestClassName, out _);
    }

    // Recreated per test to give each test an isolated cookie jar / session.
    [TestInitialize]
    public virtual Task SetupTestContext()
    {
        var port = Ports[GetType().FullName!];
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        Http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{port}")
        };
        return Task.CompletedTask;
    }

    [TestCleanup]
    public virtual void CleanTestContext()
    {
        Http.Dispose();
    }

    protected async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            response = await Http.GetAsync("/");
        }
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    protected async Task<HttpResponseMessage> PostForm(string url, Dictionary<string, string> data, string? tokenUrl = null, bool includeToken = true)
    {
        if (includeToken && !data.ContainsKey("__RequestVerificationToken"))
        {
            var token = await GetAntiCsrfToken(tokenUrl ?? url);
            data["__RequestVerificationToken"] = token;
        }
        return await Http.PostAsync(url, new FormUrlEncodedContent(data));
    }

    protected void AssertRedirect(HttpResponseMessage response, string expectedLocation, bool exact = true)
    {
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var actualLocation = response.Headers.Location?.OriginalString ?? string.Empty;
        var baseUri = Http.BaseAddress?.ToString() ?? "____";

        if (actualLocation.StartsWith(baseUri))
        {
            actualLocation = actualLocation.Substring(baseUri.Length - 1); // Keep the leading slash
        }

        if (exact)
        {
            Assert.AreEqual(expectedLocation, actualLocation, $"Expected redirect to {expectedLocation}, but was {actualLocation}");
        }
        else
        {
            Assert.StartsWith(expectedLocation, actualLocation);
        }
    }

    protected async Task LoginAsAdmin()
    {
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", "admin@default.com" },
            { "Password", "Admin@123456!" }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
    }

    protected async Task<(string email, string password)> RegisterAndLoginAsync()
    {
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        return (email, password);
    }

    protected T GetService<T>() where T : notnull
    {
        var host = Server ?? throw new InvalidOperationException("Server is not started.");
        var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }
}
