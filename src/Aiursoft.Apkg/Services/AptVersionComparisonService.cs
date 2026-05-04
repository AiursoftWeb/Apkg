using System.Text.RegularExpressions;

namespace Aiursoft.Apkg.Services;

/// <summary>
/// Implements Debian version comparison according to Policy Manual section 5.6.12
/// https://www.debian.org/doc/debian-policy/ch-controlfields.html#version
/// </summary>
public partial class AptVersionComparisonService
{
    [GeneratedRegex(@"^(?:(\d+):)?(.+?)(?:-([^-]+))?$")]
    private static partial Regex VersionRegex();

    /// <summary>
    /// Parse Debian version string into (epoch, upstream, revision)
    /// </summary>
    private (int epoch, string upstream, string revision) ParseVersion(string version)
    {
        var match = VersionRegex().Match(version);
        if (!match.Success)
        {
            throw new ArgumentException($"Invalid Debian version format: {version}");
        }

        var epoch = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        var upstream = match.Groups[2].Value;
        var revision = match.Groups[3].Success ? match.Groups[3].Value : "0";

        return (epoch, upstream, revision);
    }

    /// <summary>
    /// Compare two version strings according to Debian rules
    /// Returns: -1 if v1 &lt; v2, 0 if v1 == v2, 1 if v1 &gt; v2
    /// </summary>
    public int Compare(string version1, string version2)
    {
        var (epoch1, upstream1, revision1) = ParseVersion(version1);
        var (epoch2, upstream2, revision2) = ParseVersion(version2);

        // Compare epochs first
        if (epoch1 != epoch2)
        {
            return epoch1.CompareTo(epoch2);
        }

        // Compare upstream versions
        var upstreamCmp = CompareVersionPart(upstream1, upstream2);
        if (upstreamCmp != 0)
        {
            return upstreamCmp;
        }

        // Compare revisions
        return CompareVersionPart(revision1, revision2);
    }

    /// <summary>
    /// Compare a single version part (upstream or revision) using Debian's lexicographical rules
    /// Letters are compared lexically, digits numerically, ~ sorts before anything
    /// </summary>
    private int CompareVersionPart(string part1, string part2)
    {
        int i = 0, j = 0;

        while (i < part1.Length || j < part2.Length)
        {
            // Compare non-digit prefixes lexically
            while ((i < part1.Length && !char.IsDigit(part1[i])) ||
                   (j < part2.Length && !char.IsDigit(part2[j])))
            {
                int ac = i < part1.Length ? CharOrder(part1[i]) : 0;
                int bc = j < part2.Length ? CharOrder(part2[j]) : 0;

                if (ac != bc)
                {
                    return ac - bc;
                }

                i++;
                j++;
            }

            // Compare digit sequences numerically
            // Skip leading zeros
            while (i < part1.Length && part1[i] == '0') i++;
            while (j < part2.Length && part2[j] == '0') j++;

            // Compare digit-by-digit while both sides have digits
            int firstDiff = 0;
            while (i < part1.Length && char.IsDigit(part1[i]) &&
                   j < part2.Length && char.IsDigit(part2[j]))
            {
                if (firstDiff == 0)
                {
                    firstDiff = (int)char.GetNumericValue(part1[i]) - (int)char.GetNumericValue(part2[j]);
                }

                i++;
                j++;
            }

            // Longer digit sequence (after stripping zeros) is numerically larger
            if (i < part1.Length && char.IsDigit(part1[i]))
                return 1;
            if (j < part2.Length && char.IsDigit(part2[j]))
                return -1;

            if (firstDiff != 0)
            {
                return firstDiff;
            }
        }

        return 0;
    }

    /// <summary>
    /// Character ordering for Debian version comparison:
    /// ~ sorts before everything (including end-of-string),
    /// digits and end-of-string sort equally (as 0),
    /// then letters, then everything else.
    /// Matches dpkg's order() function.
    /// </summary>
    private int CharOrder(char c)
    {
        if (c == '~')
            return -1;
        if (char.IsDigit(c))
            return 0;
        if (char.IsLetter(c))
            return c;
        return c + 256;
    }

    /// <summary>
    /// Check if version satisfies a dependency constraint
    /// Constraint format: "&gt;= 1.2.3", "&lt;&lt; 2.0", "= 1.5", etc.
    /// </summary>
    public bool SatisfiesConstraint(string installedVersion, string constraintString)
    {
        // Parse constraint: ">> 1.2.3" -> operator=">>", version="1.2.3"
        var parts = constraintString.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            // No version constraint, any version satisfies
            return true;
        }

        var op = parts[0];
        var requiredVersion = parts[1].Trim('(', ')');

        var cmp = Compare(installedVersion, requiredVersion);

        return op switch
        {
            "<<" => cmp < 0,  // strictly earlier
            "<=" => cmp <= 0,
            "=" => cmp == 0,
            ">=" => cmp >= 0,
            ">>" => cmp > 0,  // strictly later
            _ => throw new ArgumentException($"Unknown version operator: {op}")
        };
    }
}
