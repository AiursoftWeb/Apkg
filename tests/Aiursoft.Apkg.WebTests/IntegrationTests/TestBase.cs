using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.Apkg.Entities;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

public abstract class TestBase
{
    // Shared for the lifetime of each test class. Safe because
    // [assembly:DoNotParallelize] ensures only one class runs at a time,
    // so ClassSetup/ClassTeardown never overlap across different classes.
    private static IHost? _host;
    private static int _port;

    protected HttpClient Http = null!;
    protected IHost? Server => _host;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassSetup(TestContext _)
    {
        _host = TestAssemblySetup.Host;
        _port = TestAssemblySetup.Port;

        // Reset repo/mirror/apt-package data (fast — GPG cert already exists)
        await _host!.SeedMirrorsAsync(true);

        // Reset per-class mutable state so tests don't bleed into each other
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();
        db.LocalPackages.RemoveRange(db.LocalPackages);
        db.UserApiKeys.RemoveRange(db.UserApiKeys);
        // Delete all settings so SeedAsync() re-creates them at their default values
        db.GlobalSettings.RemoveRange(db.GlobalSettings);
        await db.SaveChangesAsync();

        // Re-seed settings (and skip user/role creation since they already exist)
        await _host.SeedAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static Task ClassTeardown()
    {
        return Task.CompletedTask; // server lifetime is managed by TestAssemblySetup
    }

    // Recreated per test to give each test an isolated cookie jar / session.
    [TestInitialize]
    public virtual Task SetupTestContext()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        Http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
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
        if (_host == null) throw new InvalidOperationException("Server is not started.");
        var scope = _host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<T>();
    }
}
