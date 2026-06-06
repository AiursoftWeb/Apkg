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
    /// Validates all Dependency and Recommend declarations for every target suite.
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

        // Collect entries to check (Dependencies + Recommends)
        var entries = project.Dependencies
            .Select(d => (Kind: "Dependency", d.Value))
            .Concat(project.Recommends.Select(r => (Kind: "Recommend", r.Value)))
            .ToList();

        if (entries.Count == 0)
            return issues;

        // Determine target arch for Packages.gz lookup
        var arch = project.ArchList.FirstOrDefault(a =>
            !a.Equals("all", StringComparison.OrdinalIgnoreCase)) ?? "amd64";

        foreach (var suite in project.SuiteList)
        {
            var ctx = ConditionEvaluator.BuildContext(
                project.TargetDistro, suite, arch,
                upstreamDistro: project.UpstreamDistro,
                upstreamSuite: project.UpstreamSuite,
                upstreamArch: project.UpstreamArch);

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
                        source.Url, checkSuite, arch, ct);
                    sourceResults.Add((source, packages));
                }
                catch (Exception ex)
                {
                    issues.Add(new LintIssue(Severity.Warning,
                        $"Could not fetch package index for suite '{checkSuite}' " +
                        $"from '{source.Url}': {ex.Message}"));
                }
            }

            foreach (var (kind, depValue) in entries)
            {
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
                        $"DependencyCheckSource for suite '{suite}'. " +
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
