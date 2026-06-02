namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Evaluates MSBuild-style conditions used in .aosproj files.
/// Supported syntax:
///   '$(Property)' == 'value'
///   '$(Property)' != 'value'
///   '&lt;expr&gt;' and '&lt;expr&gt;'
///   '&lt;expr&gt;' or '&lt;expr&gt;'
/// Absent condition (null/empty) always evaluates to true.
/// </summary>
public class ConditionEvaluator
{
    /// <summary>
    /// Evaluates a condition string against a set of build properties.
    /// </summary>
    /// <param name="condition">The condition text, e.g. "'$(Suite)' == 'jammy' and '$(Arch)' == 'amd64'"</param>
    /// <param name="properties">Key-value pairs for substitution, e.g. {"Suite","jammy"}</param>
    public bool Evaluate(string? condition, IReadOnlyDictionary<string, string> properties)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        // Substitute $(Property) references first
        var expr = condition.Trim();
        foreach (var (key, value) in properties)
            expr = expr.Replace($"$({key})", value, StringComparison.OrdinalIgnoreCase);

        // Split by 'or' (lowest precedence)
        var orIdx = FindOperator(expr, " or ");
        if (orIdx >= 0)
            return EvaluateOne(expr[..orIdx]) || EvaluateOne(expr[(orIdx + 4)..]);

        // Split by 'and'
        var andIdx = FindOperator(expr, " and ");
        if (andIdx >= 0)
            return EvaluateOne(expr[..andIdx]) && EvaluateOne(expr[(andIdx + 5)..]);

        return EvaluateOne(expr);
    }

    private static bool EvaluateOne(string expr)
    {
        expr = expr.Trim();

        if (expr.Contains("=="))
        {
            var idx = expr.IndexOf("==", StringComparison.Ordinal);
            return string.Equals(
                Unquote(expr[..idx]),
                Unquote(expr[(idx + 2)..]),
                StringComparison.OrdinalIgnoreCase);
        }

        if (expr.Contains("!="))
        {
            var idx = expr.IndexOf("!=", StringComparison.Ordinal);
            return !string.Equals(
                Unquote(expr[..idx]),
                Unquote(expr[(idx + 2)..]),
                StringComparison.OrdinalIgnoreCase);
        }

        throw new InvalidOperationException(
            $"Unsupported condition syntax: '{expr}'. Only '==', '!=', 'and', 'or' are supported.");
    }

    /// <summary>Finds the outermost operator, respecting single-quoted strings.</summary>
    private static int FindOperator(string expr, string op)
    {
        var inQuote = false;
        for (var i = 0; i < expr.Length - op.Length + 1; i++)
        {
            if (expr[i] == '\'') inQuote = !inQuote;
            if (!inQuote && string.Compare(expr, i, op, 0, op.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return i;
        }
        return -1;
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
