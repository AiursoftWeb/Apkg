using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Aiursoft.Apkg.Services;
using Aiursoft.Apkg.Services.FileStorage;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.Apkg;

[ExcludeFromCodeCoverage]
public static class ProgramExtends
{
    [ExcludeFromCodeCoverage]
    private static async Task<bool> ShouldSeedAsync(TemplateDbContext dbContext)
    {
        var haveUsers = await dbContext.Users.AnyAsync();
        var haveRoles = await dbContext.Roles.AnyAsync();
        return !haveUsers && !haveRoles;
    }

    [ExcludeFromCodeCoverage]
    public static Task<IHost> CopyAvatarFileAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var storageService = services.GetRequiredService<StorageService>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        var avatarFilePath = Path.Combine(host.Services.GetRequiredService<IHostEnvironment>().ContentRootPath,
            "wwwroot", "images", "default-avatar.jpg");
        var physicalPath = storageService.GetFilePhysicalPath(User.DefaultAvatarPath);
        if (!File.Exists(avatarFilePath))
        {
            logger.LogWarning("Avatar file does not exist. Skip copying.");
            return Task.FromResult(host);
        }

        if (File.Exists(physicalPath))
        {
            logger.LogInformation("Avatar file already exists. Skip copying.");
            return Task.FromResult(host);
        }

        if (!Directory.Exists(Path.GetDirectoryName(physicalPath)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(physicalPath)!);
        }

        File.Copy(avatarFilePath, physicalPath);
        logger.LogInformation("Avatar file copied to {Path}", physicalPath);
        return Task.FromResult(host);
    }

    [ExcludeFromCodeCoverage]
    public static async Task<IHost> SeedAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        var settingsService = services.GetRequiredService<GlobalSettingsService>();
        await settingsService.SeedSettingsAsync();

        var shouldSeed = await ShouldSeedAsync(db);
        if (!shouldSeed)
        {
            logger.LogInformation("Do not need to seed the database. There are already users or roles present.");
            return host;
        }

        logger.LogInformation("Seeding the database with initial data...");
        var userManager = services.GetRequiredService<UserManager<User>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        var role = await roleManager.FindByNameAsync("Administrators");
        if (role == null)
        {
            role = new IdentityRole("Administrators");
            await roleManager.CreateAsync(role);
        }

        var existingClaims = await roleManager.GetClaimsAsync(role);
        var existingClaimValues = existingClaims
            .Where(c => c.Type == AppPermissions.Type)
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var permission in AppPermissions.GetAllPermissions())
        {
            if (!existingClaimValues.Contains(permission.Key))
            {
                var claim = new Claim(AppPermissions.Type, permission.Key);
                await roleManager.AddClaimAsync(role, claim);
            }
        }

        if (!await db.Users.AnyAsync(u => u.UserName == "admin"))
        {
            var user = new User
            {
                UserName = "admin",
                DisplayName = "Super Administrator",
                Email = "admin@default.com",
            };
            _ = await userManager.CreateAsync(user, "admin123");
            await userManager.AddToRoleAsync(user, "Administrators");
        }

        await host.SeedMirrorsAsync();
        return host;
    }

    [ExcludeFromCodeCoverage]
    public static async Task<IHost> SeedMirrorsAsync(this IHost host, bool force = false)
    {
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<TemplateDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        var signingService = services.GetRequiredService<IGpgSigningService>();

        logger.LogInformation("Seeding the database with new Pipeline V2 structure...");

        // 1. Ensure a default certificate exists
        var cert = await db.AptCertificates.FirstOrDefaultAsync();
        if (cert == null)
        {
            logger.LogInformation("Generating default GPG certificate...");
            var (pub, priv, fpr) = await signingService.GenerateKeyPairAsync("Apkg Default Certificate <support@aiursoft.com>");
            cert = new AptCertificate
            {
                Name = "anduinos",
                FriendlyName = "Anduinos Official Key",
                PublicKey = pub,
                PrivateKey = priv,
                Fingerprint = fpr
            };
            db.AptCertificates.Add(cert);
            await db.SaveChangesAsync();
        }

        // Link existing mirrors if they don't have one
        // (Simplified for seeder)

        if (force)
        {
            db.AptPackages.RemoveRange(db.AptPackages);
            db.AptBuckets.RemoveRange(db.AptBuckets);
            db.AptRepositories.RemoveRange(db.AptRepositories);
            db.AptMirrors.RemoveRange(db.AptMirrors);
            await db.SaveChangesAsync();
        }
        else if (await db.AptMirrors.AnyAsync())
        {
            return host;
        }

        var baseUrl = "https://mirror.aiursoft.com/";
        var components = "main,restricted,universe,multiverse";
        var suites = new[] { "questing", "questing-updates", "questing-backports", "questing-security" };

        foreach (var suite in suites)
        {
            // Create a mirror for the upstream ubuntu suite
            var mirror = new AptMirror
            {
                BaseUrl = baseUrl,
                Distro = "ubuntu", // 进货 Distro 是 ubuntu
                Suite = suite,    // 进货 Suite 是 questing
                Components = components,
                Architecture = "amd64"
            };
            db.AptMirrors.Add(mirror);
            await db.SaveChangesAsync();

            // Create a repository that presents as 'anduinos' distro but keeps the original suite name
            var repo = new AptRepository
            {
                Distro = "anduinos", // 卖货 Distro 改为 anduinos
                Name = $"Anduinos Official {suite}", // 卖货 Repository 叫 Anduinos Official questing
                Suite = suite,      // 卖货 Suite 依然叫 questing
                Components = components,
                CertificateId = cert.Id,
                MirrorId = mirror.Id
            };
            db.AptRepositories.Add(repo);
        }

        await db.SaveChangesAsync();
        return host;
    }
}
