namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Evaluates MSBuild-style conditions used in .aosproj files.
/// Supported syntax:
///   '$(Property)' == 'value'
///   '$(Property)' != 'value'
/// Absent condition (null/empty) always evaluates to true.
/// </summary>
public class ConditionEvaluator
{
    /// <summary>
    /// Evaluates a condition string against a set of build properties.
    /// </summary>
    /// <param name="condition">The condition text, e.g. "'$(Suite)' == 'jammy'"</param>
    /// <param name="properties">Key-value pairs for substitution, e.g. {"Suite","jammy"}</param>
    public bool Evaluate(string? condition, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var expr = condition.Trim();

        // Substitute $(Property) references
        foreach (var (key, value) in properties)
            expr = expr.Replace($"$({key})", value, StringComparison.OrdinalIgnoreCase);

        // Try == comparison
        var eqIdx = expr.IndexOf("==", StringComparison.Ordinal);
        if (eqIdx >= 0)
        {
            var left = Unquote(expr[..eqIdx]);
            var right = Unquote(expr[(eqIdx + 2)..]);
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        // Try != comparison
        var neqIdx = expr.IndexOf("!=", StringComparison.Ordinal);
        if (neqIdx >= 0)
        {
            var left = Unquote(expr[..neqIdx]);
            var right = Unquote(expr[(neqIdx + 2)..]);
            return !string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException(
            $"Unsupported condition syntax: '{condition}'. Only '==' and '!=' are supported.");
    }

    private static string Unquote(string s) => s.Trim().Trim('\'', '"');

    public static IReadOnlyDictionary<string, string> BuildContext(
        string distro, string suite, string arch,
        string? upstreamDistro = null,
        string? upstreamSuite = null,
        string? upstreamArch = null,
        string? component = null) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Distro"] = distro,
            ["Suite"] = suite,
            ["Arch"] = arch,
            ["Architecture"] = arch,
            ["Component"] = component ?? string.Empty,
            ["UpstreamDistro"] = upstreamDistro ?? string.Empty,
            ["UpstreamSuite"] = upstreamSuite ?? string.Empty,
            ["UpstreamArch"] = upstreamArch ?? string.Empty,
            ["UpstreamArchitecture"] = upstreamArch ?? string.Empty,
        };
}
