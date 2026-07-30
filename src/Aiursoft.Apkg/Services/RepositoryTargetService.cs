using System.Security.Claims;
using Aiursoft.Apkg.Authorization;
using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Provides the canonical repository matching and upload permission rules used
/// by both preflight checks and actual uploads.
/// </summary>
public sealed class RepositoryTargetService(ApkgDbContext db)
{
    public async Task<IReadOnlyList<AptRepository>> FindMatchingAsync(
        string distro,
        string suite,
        string component,
        string architecture)
    {
        var repositories = await db.AptRepositories
            .Where(repository => repository.Distro == distro && repository.Suite == suite)
            .ToListAsync();

        return repositories
            .Where(repository =>
                repository.Components
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains(component, StringComparer.OrdinalIgnoreCase)
                && ArchitectureMatches(repository.Architecture, architecture))
            .ToList();
    }

    public static bool CanUpload(AptRepository repository, ClaimsPrincipal user)
    {
        return repository.AllowAnyoneToUpload
               || user.HasClaim(AppPermissions.Type, AppPermissionNames.CanManageRepositories)
               || user.HasClaim(
                   AppPermissions.Type,
                   AppPermissionNames.CanUploadToRestrictedRepositories);
    }

    public static bool ArchitectureMatches(
        string repositoryArchitectures,
        string targetArchitecture)
    {
        if (string.Equals(targetArchitecture, "all", StringComparison.OrdinalIgnoreCase))
            return true;

        return repositoryArchitectures
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(architecture => string.Equals(
                architecture, targetArchitecture, StringComparison.OrdinalIgnoreCase));
    }
}
