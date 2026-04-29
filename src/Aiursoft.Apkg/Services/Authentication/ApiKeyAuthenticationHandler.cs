using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Aiursoft.Apkg.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.Apkg.Services.Authentication;

/// <summary>
/// Authenticates requests that carry an API key in the Authorization: Bearer header.
/// The raw key is never stored; only its SHA-256 hex digest is persisted in the database.
/// </summary>
public class ApiKeyAuthenticationHandler(
    ApkgDbContext db,
    IUserClaimsPrincipalFactory<User> claimsPrincipalFactory,
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var headerValue = authHeader.ToString();
        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var rawKey = headerValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.Fail("Empty API key.");

        var keyHash = ComputeSha256Hex(rawKey);

        var apiKey = await db.UserApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash);

        if (apiKey?.User == null)
            return AuthenticateResult.Fail("Invalid API key.");

        if (apiKey.IsExpired)
            return AuthenticateResult.Fail("API key has expired.");

        // Update last-used timestamp (fire-and-forget style; non-critical)
        apiKey.LastUsedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Build the full ClaimsPrincipal using the same factory as cookie auth,
        // so all role-based permission claims are included.
        var principal = await claimsPrincipalFactory.CreateAsync(apiKey.User);

        // Wrap the identity with the ApiKey scheme name so ASP.NET Core knows how it was authenticated.
        var identity = new ClaimsIdentity(principal.Claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    public static string ComputeSha256Hex(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
