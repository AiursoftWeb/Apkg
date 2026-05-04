using Aiursoft.Apkg.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Aiursoft.Apkg.Services.BackgroundJobs;

/// <summary>
/// Background job to check repository dependency integrity.
/// Verifies all packages (including virtual) have their dependencies satisfied.
/// </summary>
public class RepositoryDependencyCheckJob(
    ILogger<RepositoryDependencyCheckJob> logger,
    IServiceScopeFactory serviceScopeFactory,
    AptVersionComparisonService versionCompare)
{
    public async Task<int> RunAsync(int repositoryId, CancellationToken cancellationToken = default)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApkgDbContext>();

        // Create report entry
        var report = new DependencyCheckReport
        {
            RepositoryId = repositoryId,
            Status = "Running",
            CreatedAt = DateTime.UtcNow,
            ExpireAt = DateTime.UtcNow.AddHours(72)
        };
        db.DependencyCheckReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);
        var reportId = report.Id;

        try
        {
            logger.LogInformation("Starting dependency check for repository {RepoId}, report {ReportId}", repositoryId, reportId);

            // Get repository with its primary bucket
            var repository = await db.AptRepositories
                .Include(r => r.PrimaryBucket)
                .ThenInclude(b => b!.Packages)
                .FirstOrDefaultAsync(r => r.Id == repositoryId, cancellationToken);

            if (repository?.PrimaryBucket == null)
            {
                throw new InvalidOperationException($"Repository {repositoryId} has no primary bucket");
            }

            var packages = repository.PrimaryBucket.Packages.ToList();
            logger.LogInformation("Checking {Count} packages in repository {RepoId}", packages.Count, repositoryId);

            // Build package availability index: (name, arch) -> list of available versions
            var availablePackages = packages
                .GroupBy(p => (p.Package, p.Architecture))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(p => p.Version).ToList()
                );

            var problematicPackages = new List<PackageDependencyIssue>();

            // Check each package's dependencies
            foreach (var pkg in packages)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                var missingDeps = await CheckPackageDependencies(pkg, availablePackages, cancellationToken);
                if (missingDeps.Count > 0)
                {
                    problematicPackages.Add(new PackageDependencyIssue
                    {
                        Package = pkg.Package,
                        Version = pkg.Version,
                        Architecture = pkg.Architecture,
                        IsVirtual = pkg.IsVirtual,
                        MissingDeps = missingDeps
                    });
                }
            }

            // Update report
            report = await db.DependencyCheckReports.FindAsync([reportId], cancellationToken);
            if (report == null)
            {
                throw new InvalidOperationException($"Report {reportId} disappeared during execution");
            }

            report.TotalPackages = packages.Count;
            report.ProblematicPackages = problematicPackages.Count;
            report.DetailsJson = JsonSerializer.Serialize(problematicPackages, new JsonSerializerOptions
            {
                WriteIndented = false
            });
            report.Status = "Completed";
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Dependency check completed for repository {RepoId}: {Total} packages, {Problematic} with issues",
                repositoryId, packages.Count, problematicPackages.Count);

            return reportId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dependency check failed for repository {RepoId}", repositoryId);

            // Update report with error
            var failedReport = await db.DependencyCheckReports.FindAsync([reportId], cancellationToken);
            if (failedReport != null)
            {
                failedReport.Status = "Failed";
                var errorMsg = ex.Message;
                if (errorMsg.Length > 2000)
                {
                    errorMsg = errorMsg.Substring(0, 1997) + "...";
                }
                failedReport.ErrorMessage = errorMsg;
                await db.SaveChangesAsync(CancellationToken.None);
            }

            throw;
        }
    }

    private Task<List<MissingDependency>> CheckPackageDependencies(
        AptPackage package,
        Dictionary<(string Package, string Architecture), List<string>> availablePackages,
        CancellationToken _)
    {
        var missingDeps = new List<MissingDependency>();

        if (string.IsNullOrWhiteSpace(package.Depends))
        {
            return Task.FromResult(missingDeps);
        }

        // Parse Depends field: "foo (>= 1.2), bar | baz, qux"
        // Split by comma for AND dependencies
        var andGroups = package.Depends.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var andGroup in andGroups)
        {
            // Split by | for OR alternatives
            var orAlternatives = andGroup.Split('|', StringSplitOptions.RemoveEmptyEntries);

            bool satisfied = false;
            string? bestMatchInfo = null;

            foreach (var alternative in orAlternatives)
            {
                var trimmed = alternative.Trim();
                var (depName, constraint, depArch) = ParseDependency(trimmed);

                // Check if this alternative is satisfied
                var key = (depName, depArch ?? package.Architecture);
                if (availablePackages.TryGetValue(key, out var versions))
                {
                    // Check if any available version satisfies the constraint
                    if (string.IsNullOrWhiteSpace(constraint))
                    {
                        // No version constraint, any version satisfies
                        satisfied = true;
                        break;
                    }

                    foreach (var availableVersion in versions)
                    {
                        try
                        {
                            if (versionCompare.SatisfiesConstraint(availableVersion, constraint))
                            {
                                satisfied = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning("Failed to compare versions: {AvailVer} vs {Constraint}: {Error}",
                                availableVersion, constraint, ex.Message);
                        }
                    }

                    if (satisfied)
                    {
                        break;
                    }

                    // Not satisfied, record best available version
                    var bestVersion = versions.OrderByDescending(v => v).FirstOrDefault();
                    bestMatchInfo = bestVersion;
                }
                else
                {
                    // Package not available at all
                    bestMatchInfo = null;
                }
            }

            if (!satisfied)
            {
                // None of the OR alternatives were satisfied
                var alternatives = string.Join(" | ", orAlternatives.Select(a => a.Trim()));
                missingDeps.Add(new MissingDependency
                {
                    Required = alternatives,
                    Available = bestMatchInfo ?? "not found"
                });
            }
        }

        return Task.FromResult(missingDeps);
    }

    /// <summary>
    /// Parse dependency string: "foo (>= 1.2.3) [amd64]" -> (name, constraint, arch)
    /// </summary>
    private (string name, string? constraint, string? arch) ParseDependency(string dep)
    {
        var trimmed = dep.Trim();

        // Extract architecture: [amd64]
        string? arch = null;
        var archStart = trimmed.IndexOf('[');
        if (archStart >= 0)
        {
            var archEnd = trimmed.IndexOf(']', archStart);
            if (archEnd > archStart)
            {
                arch = trimmed[(archStart + 1)..archEnd].Trim();
                trimmed = trimmed[..archStart].Trim();
            }
        }

        // Extract version constraint: (>= 1.2.3)
        string? constraint = null;
        var constraintStart = trimmed.IndexOf('(');
        if (constraintStart >= 0)
        {
            var constraintEnd = trimmed.IndexOf(')', constraintStart);
            if (constraintEnd > constraintStart)
            {
                constraint = trimmed[(constraintStart + 1)..constraintEnd].Trim();
                trimmed = trimmed[..constraintStart].Trim();
            }
        }

        return (trimmed, constraint, arch);
    }

    private class PackageDependencyIssue
    {
        public required string Package { get; set; }
        public required string Version { get; set; }
        public required string Architecture { get; set; }
        public bool IsVirtual { get; set; }
        public required List<MissingDependency> MissingDeps { get; set; }
    }

    private class MissingDependency
    {
        public required string Required { get; set; }
        public required string Available { get; set; }
    }
}
