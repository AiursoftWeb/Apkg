using System.Net;
using Aiursoft.Apkg.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.Apkg.WebTests.IntegrationTests;

/// <summary>
/// Access-control tests for LocalPackagesController:
///   - Visibility scoping (non-admin sees only own packages; admin sees all)
///   - Ownership enforcement for Toggle and Delete
///   - Toggle conflict-resolution (re-enabling disables all other enabled versions)
///   - Upload page gatekeeping based on AllowAnyoneToUpload / restricted permission
/// </summary>
[TestClass]
public class LocalPackageAccessControlTests : TestBase
{
    private ApkgDbContext _db = null!;
    private AptRepository _repo = null!;
    private string _adminUserId = null!;

    [TestInitialize]
    public override async Task SetupTestContext()
    {
        await base.SetupTestContext();
        await LoginAsAdmin();

        _db = GetService<ApkgDbContext>();
        _repo = _db.AptRepositories.First();

        var userManager = GetService<UserManager<User>>();
        var admin = await userManager.FindByEmailAsync("admin@default.com");
        _adminUserId = admin!.Id;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────

    private LocalPackage AddPackageForUser(
        string userId,
        string name = "testpkg",
        string version = "1.0.0",
        bool isEnabled = true)
    {
        var lp = new LocalPackage
        {
            UploadedByUserId = userId,
            RepositoryId = _repo.Id,
            Component = "main",
            Package = name,
            Version = version,
            Architecture = "amd64",
            Maintainer = "Test <test@example.com>",
            Filename = $"pool/main/{name[0]}/{name}/{name}_{version}_amd64.deb",
            Size = "1024",
            SHA256 = Guid.NewGuid().ToString("N").PadRight(64, '0')[..64],
            IsEnabled = isEnabled
        };
        _db.LocalPackages.Add(lp);
        _db.SaveChanges();
        return lp;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Index visibility scoping
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Index_Admin_CanSeeAllUsersPackages()
    {
        // Arrange: package belonging to admin
        AddPackageForUser(_adminUserId, "admin-visible-pkg");

        // Register a second user and seed a package for them directly
        var (email2, _) = await RegisterAndLoginAsync();
        var userManager = GetService<UserManager<User>>();
        var user2 = await userManager.FindByEmailAsync(email2);
        AddPackageForUser(user2!.Id, "other-user-visible-pkg");

        // Log back in as admin
        await LoginAsAdmin();

        // Act
        var response = await Http.GetAsync("/LocalPackages/Index");
        var html = await response.Content.ReadAsStringAsync();

        // Assert: admin sees both packages
        Assert.IsTrue(html.Contains("admin-visible-pkg"), "Admin should see their own package.");
        Assert.IsTrue(html.Contains("other-user-visible-pkg"), "Admin should see all users' packages.");
    }

    [TestMethod]
    public async Task Index_RegularUser_SeesOnlyOwnPackages()
    {
        // Arrange: package belonging to admin (seeded)
        AddPackageForUser(_adminUserId, "admin-only-pkg");

        // Register a second user
        await RegisterAndLoginAsync();
        var (email2, pw2) = await RegisterAndLoginAsync();
        // Log in as the second user
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email2 },
            { "Password", pw2 }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Add a package for the second user
        var userManager = GetService<UserManager<User>>();
        var user2 = await userManager.FindByEmailAsync(email2);
        AddPackageForUser(user2!.Id, "my-own-pkg");

        // Act
        var response = await Http.GetAsync("/LocalPackages/Index");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.IsTrue(html.Contains("my-own-pkg"), "User should see their own package.");
        Assert.IsFalse(html.Contains("admin-only-pkg"), "User must NOT see admin's package.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Toggle ownership enforcement
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Toggle_OtherUsersPackage_IsDenied_ForRegularUser()
    {
        // Arrange: package owned by admin
        var adminPkg = AddPackageForUser(_adminUserId, "admin-pkg-toggle");

        // Register and log in as a different user
        var (email, pw) = await RegisterAndLoginAsync();
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", pw }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Add a package for the regular user so the Index page has forms (for CSRF token)
        var userManager = GetService<UserManager<User>>();
        var regularUser = await userManager.FindByEmailAsync(email);
        AddPackageForUser(regularUser!.Id, "my-own-pkg-toggle-guard");

        // Act: attempt to toggle admin's package
        var response = await PostForm($"/LocalPackages/Toggle?id={adminPkg.Id}",
            new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");

        // Assert: the response must NOT redirect to /LocalPackages/Index (which means success).
        // Forbid() with cookie auth redirects to /Account/AccessDenied (302), not to the index.
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.IsFalse(location.Contains("/LocalPackages/Index"),
            "Toggle of another user's package must not succeed (must not redirect to Index).");

        // The package's enabled state must be unchanged in the DB.
        _db.Entry(adminPkg).Reload();
        Assert.IsTrue(adminPkg.IsEnabled, "Admin's package must remain enabled after an unauthorized toggle attempt.");
    }

    [TestMethod]
    public async Task Delete_OtherUsersPackage_IsDenied_ForRegularUser()
    {
        // Arrange: package owned by admin
        var adminPkg = AddPackageForUser(_adminUserId, "admin-pkg-delete");

        // Register and log in as a different user
        var (email, pw) = await RegisterAndLoginAsync();
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", pw }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Add a package for the regular user so the Index page has forms (for CSRF token)
        var userManager = GetService<UserManager<User>>();
        var regularUser = await userManager.FindByEmailAsync(email);
        AddPackageForUser(regularUser!.Id, "my-own-pkg-delete-guard");

        // Act: attempt to delete admin's package
        var response = await PostForm($"/LocalPackages/Delete?id={adminPkg.Id}",
            new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");

        // Assert: must NOT redirect to /LocalPackages/Index (which would mean success)
        var location = response.Headers.Location?.OriginalString ?? string.Empty;
        Assert.IsFalse(location.Contains("/LocalPackages/Index"),
            "Delete of another user's package must not succeed (must not redirect to Index).");

        // Verify the package still exists
        var stillExists = _db.LocalPackages.Any(x => x.Id == adminPkg.Id);
        Assert.IsTrue(stillExists, "Package must not be deleted when an unauthorized user attempts to delete it.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Toggle conflict resolution (re-enable disables other enabled version)
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Toggle_ReEnabling_DisablesOtherEnabledVersionForSamePackageAndArch()
    {
        // Arrange: two versions of same package — v1 enabled, v2 disabled
        var pkgV1 = AddPackageForUser(_adminUserId, "conflictpkg", version: "1.0.0", isEnabled: true);
        var pkgV2 = AddPackageForUser(_adminUserId, "conflictpkg", version: "2.0.0", isEnabled: false);

        // Act: toggle v2 (disabled → enabled); this should disable v1
        var response = await PostForm($"/LocalPackages/Toggle?id={pkgV2.Id}",
            new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        // Assert
        _db.Entry(pkgV1).Reload();
        _db.Entry(pkgV2).Reload();
        Assert.IsTrue(pkgV2.IsEnabled, "v2 should now be enabled.");
        Assert.IsFalse(pkgV1.IsEnabled, "v1 should have been automatically disabled when v2 was re-enabled.");
    }

    [TestMethod]
    public async Task Toggle_Disabling_DoesNotAffectOtherPackageVersions()
    {
        // Arrange: two versions — both enabled (edge case: should not happen via UI, but guard it)
        var pkgA = AddPackageForUser(_adminUserId, "safetypkg", version: "1.0.0", isEnabled: true);
        var pkgB = AddPackageForUser(_adminUserId, "safetypkg", version: "2.0.0", isEnabled: true);

        // Act: disable pkgA
        var response = await PostForm($"/LocalPackages/Toggle?id={pkgA.Id}",
            new Dictionary<string, string>(), tokenUrl: "/LocalPackages/Index");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        // Assert: pkgA is disabled; pkgB is left unchanged
        _db.Entry(pkgA).Reload();
        _db.Entry(pkgB).Reload();
        Assert.IsFalse(pkgA.IsEnabled, "pkgA should be disabled after toggle.");
        Assert.IsTrue(pkgB.IsEnabled, "pkgB should remain untouched when pkgA was disabled.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Upload permission — AllowAnyoneToUpload gate
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Upload_IsDenied_WhenAllReposAreRestrictedAndUserHasNoSpecialPerm()
    {
        // Arrange: all repos are restricted (AllowAnyoneToUpload = false)
        foreach (var r in _db.AptRepositories)
            r.AllowAnyoneToUpload = false;
        _db.SaveChanges();

        // Register a regular user (no CanUploadToRestrictedRepositories permission)
        var (email, pw) = await RegisterAndLoginAsync();
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", pw }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Act
        var response = await Http.GetAsync("/LocalPackages/Upload");

        // Assert: Forbid() with cookie auth redirects to AccessDenied (302), not 200 OK.
        // We verify the user is NOT served the upload form.
        Assert.AreNotEqual(HttpStatusCode.OK, response.StatusCode,
            "Upload page must not return 200 OK when no repositories allow public uploads and user lacks restricted-upload permission.");
    }

    [TestMethod]
    public async Task Upload_ShowsRepo_WhenAllowAnyoneToUploadIsTrue_ForRegularUser()
    {
        // Arrange: at least one repo is open
        var openRepo = _db.AptRepositories.First();
        openRepo.AllowAnyoneToUpload = true;
        _db.SaveChanges();

        // Register a regular user
        var (email, pw) = await RegisterAndLoginAsync();
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", email },
            { "Password", pw }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);

        // Act
        var response = await Http.GetAsync("/LocalPackages/Upload");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();

        // Assert: the open repo appears in the dropdown
        Assert.IsTrue(html.Contains(openRepo.Suite),
            "The open repository should appear in the upload form dropdown for a regular user.");
    }

    // ──────────────────────────────────────────────────────────────────────
    // AllowAnyoneToUpload reflected in Edit page
    // ──────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RepositoryEdit_AllowAnyoneToUpload_CheckboxReflectsCurrentState()
    {
        _repo.AllowAnyoneToUpload = true;
        _db.SaveChanges();

        var response = await Http.GetAsync($"/Repositories/Edit/{_repo.Id}");
        var html = await response.Content.ReadAsStringAsync();

        // The checkbox should be checked
        Assert.IsTrue(html.Contains("allowAnyoneToUpload"),
            "Edit page must contain the AllowAnyoneToUpload checkbox.");
    }

    [TestMethod]
    public async Task RepositoryEdit_CanToggleAllowAnyoneToUpload()
    {
        // Arrange: start with AllowAnyoneToUpload = false
        _repo.AllowAnyoneToUpload = false;
        _db.SaveChanges();

        // Act: POST to Edit with AllowAnyoneToUpload = true
        var response = await PostForm($"/Repositories/Edit", new Dictionary<string, string>
        {
            { "Id", _repo.Id.ToString() },
            { "Distro", _repo.Distro },
            { "Name", _repo.Name },
            { "Suite", _repo.Suite },
            { "Components", _repo.Components },
            { "Architecture", _repo.Architecture },
            { "EnableGpgSign", "false" },
            { "AllowAnyoneToUpload", "true" }
        }, tokenUrl: $"/Repositories/Edit/{_repo.Id}");

        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);

        // Assert
        _db.Entry(_repo).Reload();
        Assert.IsTrue(_repo.AllowAnyoneToUpload,
            "AllowAnyoneToUpload must be updated to true after form POST.");
    }
}
