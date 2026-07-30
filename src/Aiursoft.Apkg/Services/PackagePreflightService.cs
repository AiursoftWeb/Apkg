using System.Security.Claims;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Sdk.Models;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.Apkg.Services;

public sealed class PackagePreflightService(
    ApkgDbContext db,
    RepositoryTargetService repositoryTargets)
{
    public async Task<PackagePreflightResponse> CheckAsync(
        PackagePreflightRequest request,
        string userId,
        ClaimsPrincipal user)
    {
        var name = request.Name.Trim();
        var distro = request.Distro.Trim().ToLowerInvariant();
        var component = request.Component.Trim().ToLowerInvariant();

        var ownedPackage = await db.ApkgPackages.FirstOrDefaultAsync(package =>
            package.Name == name
            && package.Distro == distro
            && package.Component == component);
        if (ownedPackage != null && ownedPackage.OwnerUserId != userId)
        {
            var forbiddenTargets = request.Targets.Select(target =>
                CreateResult(
                    target,
                    PackagePreflightStatus.Forbidden,
                    "The package is owned by another user.",
                    [])).ToList();
            return new PackagePreflightResponse
            {
                AllPresent = false,
                Targets = forbiddenTargets
            };
        }

        var results = new List<PackagePreflightTargetResult>(request.Targets.Count);
        foreach (var target in request.Targets)
        {
            var suite = target.Suite.Trim();
            var architecture = target.Architecture.Trim();
            var version = target.Version.Trim();
            var normalizedTarget = new PackagePreflightTarget
            {
                Suite = suite,
                Architecture = architecture,
                Version = version
            };

            var matchingRepositories = await repositoryTargets.FindMatchingAsync(
                distro, suite, component, architecture);
            if (matchingRepositories.Count == 0)
            {
                results.Add(CreateResult(
                    normalizedTarget,
                    PackagePreflightStatus.NoRepository,
                    $"No repository matches {distro} {suite} {architecture} component '{component}'.",
                    []));
                continue;
            }

            var authorizedRepositories = matchingRepositories
                .Where(repository => RepositoryTargetService.CanUpload(repository, user))
                .ToList();
            if (authorizedRepositories.Count == 0)
            {
                results.Add(CreateResult(
                    normalizedTarget,
                    PackagePreflightStatus.Forbidden,
                    "The API key cannot upload to any matching repository.",
                    matchingRepositories.Select(repository =>
                        new PackagePreflightRepositoryResult
                        {
                            Id = repository.Id,
                            Name = DebUploadService.GetRepositoryDisplayName(repository),
                            Present = false
                        }).ToList()));
                continue;
            }

            var repositoryIds = authorizedRepositories.Select(repository => repository.Id).ToList();
            var presentRepositoryIds = await db.ApkgDebPackages
                .Where(package =>
                    repositoryIds.Contains(package.RepositoryId)
                    && package.Package == name
                    && package.Version == version
                    && package.Architecture == architecture
                    && package.IsEnabled)
                .Select(package => package.RepositoryId)
                .Distinct()
                .ToListAsync();
            var presentSet = presentRepositoryIds.ToHashSet();
            var repositoryResults = authorizedRepositories.Select(repository =>
                new PackagePreflightRepositoryResult
                {
                    Id = repository.Id,
                    Name = DebUploadService.GetRepositoryDisplayName(repository),
                    Present = presentSet.Contains(repository.Id)
                }).ToList();
            var allRepositoriesPresent = repositoryResults.All(repository => repository.Present);

            results.Add(CreateResult(
                normalizedTarget,
                allRepositoriesPresent
                    ? PackagePreflightStatus.Present
                    : PackagePreflightStatus.Missing,
                allRepositoriesPresent ? null : "At least one matching repository is missing this version.",
                repositoryResults));
        }

        return new PackagePreflightResponse
        {
            AllPresent = results.Count > 0
                         && results.All(result =>
                             result.Status == PackagePreflightStatus.Present),
            Targets = results
        };
    }

    private static PackagePreflightTargetResult CreateResult(
        PackagePreflightTarget target,
        PackagePreflightStatus status,
        string? message,
        IReadOnlyList<PackagePreflightRepositoryResult> repositories) => new()
    {
        Suite = target.Suite,
        Architecture = target.Architecture,
        Version = target.Version,
        Status = status,
        Message = message,
        Repositories = repositories
    };
}
