using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Validates that declared package dependencies exist in at least one of the
/// configured <see cref="AosprojProject.DependencyCheckSources"/>.
///
/// Each Dependency or Recommend entry is checked against every configured
/// source (union semantics — passing one source is enough). Sources can be
/// filtered by suite via an optional Condition attribute.
///
/// Runs asynchronously (network I/O) and is intentionally separate from the
/// synchronous <see cref="AosprojLinter"/> so the static linter has no network
/// dependency. All issues are Warnings — a network failure must never block
/// the build.
/// </summary>
public class AosprojDependencyValidator
{
    private readonly AptPackageIndexClient _indexClient;
    private readonly ConditionEvaluator _evaluator;

    public record LintIssue(Severity Level, string Message);
    public enum Severity { Warning, Error }

    public AosprojDependencyValidator(AptPackageIndexClient indexClient, ConditionEvaluator evaluator)
    {
        _indexClient = indexClient;
        _evaluator = evaluator;
    }

    /// <summary>
    /// Validates all applicable Dependency and Recommend declarations for every
    /// target suite/architecture pair.
    /// Returns Warnings for packages not found in any configured source.
    /// Returns an empty list when <see cref="AosprojProject.DependencyCheckSources"/> is empty.
    /// </summary>
    public async Task<IReadOnlyList<LintIssue>> ValidateAsync(
        AosprojProject project,
        CancellationToken ct = default)
    {
        var issues = new List<LintIssue>();

        if (project.DependencyCheckSources.Count == 0)
            return issues;

        // Keep conditions attached until a concrete build target is evaluated.
        var entries = project.Dependencies
            .Select(d => (Kind: "Dependency", Entry: d))
            .Concat(project.Recommends.Select(r => (Kind: "Recommend", Entry: r)))
            .ToList();

        if (entries.Count == 0)
            return issues;

        var architectures = project.ArchList.Length > 0
            ? project.ArchList
            : ["amd64"];

        foreach (var suite in project.SuiteList)
        foreach (var arch in architectures)
        {
            var ctx = ConditionEvaluator.BuildContext(
                project.TargetDistro, suite, arch,
                upstreamDistro: project.UpstreamDistro,
                upstreamSuite: project.UpstreamSuite,
                upstreamArch: project.UpstreamArch,
                component: project.Component);

            var applicableEntries = entries
                .Where(item => _evaluator.Evaluate(item.Entry.Condition, ctx))
                .ToList();
            if (applicableEntries.Count == 0)
                continue;

            // An Architecture: all package is present in each concrete APT
            // architecture index. amd64 is the conventional lookup fallback.
            var lookupArch = arch.Equals("all", StringComparison.OrdinalIgnoreCase)
                ? "amd64"
                : arch;

            var sourceResults = new List<(DependencyCheckSourceItem Source, IReadOnlySet<string> Packages)>();

            foreach (var source in project.DependencyCheckSources)
            {
                // Skip sources whose Condition doesn't match this suite
                if (!string.IsNullOrWhiteSpace(source.Condition) &&
                    !_evaluator.Evaluate(source.Condition, ctx))
                    continue;

                var suiteMap = source.GetSuiteMap();
                var checkSuite = suiteMap.TryGetValue(suite, out var mapped) ? mapped : suite;

                try
                {
                    var packages = await _indexClient.GetAvailablePackagesAsync(
                        source.Url, checkSuite, lookupArch, ct);
                    sourceResults.Add((source, packages));
                }
                catch (Exception ex)
                {
                    issues.Add(new LintIssue(Severity.Warning,
                        $"Could not fetch package index for suite '{checkSuite}' " +
                        $"architecture '{lookupArch}' from '{source.Url}': {ex.Message}"));
                }
            }

            foreach (var (kind, entry) in applicableEntries)
            {
                var depValue = entry.Value;
                if (string.IsNullOrWhiteSpace(depValue))
                    continue;

                var alternatives = depValue
                    .Split('|')
                    .Select(a => StripVersionConstraint(a.Trim()))
                    .Where(a => !string.IsNullOrEmpty(a))
                    .ToList();

                var found = sourceResults.Any(sr =>
                    alternatives.Any(a => sr.Packages.Contains(a)));

                if (!found)
                {
                    var pkgList = string.Join(" | ", alternatives);
                    issues.Add(new LintIssue(Severity.Warning,
                        $"{kind} '{pkgList}' not found in any configured " +
                        $"DependencyCheckSource for suite '{suite}', " +
                        $"architecture '{arch}'. " +
                        "Verify the package name is correct for this suite."));
                }
            }
        }

        return issues;
    }

    /// <summary>Strips "pkg (>= 1.0)" → "pkg".</summary>
    private static string StripVersionConstraint(string dep)
    {
        var idx = dep.IndexOf('(');
        return (idx > 0 ? dep[..idx] : dep).Trim();
    }
}
