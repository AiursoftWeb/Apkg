using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Aiursoft.Apkg.WebTests;

/// <summary>
/// Guards against String.Format-style placeholders like {arch} or {name} appearing
/// in Localizer keys or resx values without being proper numeric indices ({0}, {1}, …).
/// LocalizedHtmlString.WriteTo calls String.Format on the translated value, so any
/// {letter} placeholder causes a FormatException at runtime.
/// </summary>
[TestClass]
public class LocalizationFormatTests
{
    // Matches {word} where the first char after { is a letter/underscore.
    // The negative lookbehind (?<!{) excludes already-escaped {{ sequences.
    private static readonly Regex InvalidFormatSpec = new(
        @"(?<!\{)\{[A-Za-z_][^}]*\}",
        RegexOptions.Compiled);

    private static string GetSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.sln").Any())
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException("Solution root not found.");
    }

    [TestMethod]
    public void LocalizerKeysInViews_ShouldNotContainNonIntegerFormatSpecifiers()
    {
        var root = GetSolutionRoot();
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cshtml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            // Extract the first argument (key) of every @Localizer["…"] call
            var matches = Regex.Matches(content, @"@Localizer\[""([^""]+)""");
            foreach (Match m in matches)
            {
                var key = m.Groups[1].Value;
                if (InvalidFormatSpec.IsMatch(key))
                    violations.Add($"{Path.GetRelativePath(root, file)}: key \"{key}\"");
            }
        }

        Assert.AreEqual(0, violations.Count,
            "Localizer keys with non-integer format specifiers (would cause FormatException at render time):\n"
            + string.Join("\n", violations));
    }

    [TestMethod]
    public void ResxValues_ShouldNotContainNonIntegerFormatSpecifiers()
    {
        var root = GetSolutionRoot();
        var srcRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.resx", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            foreach (var data in doc.Descendants("data"))
            {
                var value = data.Element("value")?.Value ?? string.Empty;
                if (InvalidFormatSpec.IsMatch(value))
                {
                    var name = data.Attribute("name")?.Value ?? "(unknown)";
                    violations.Add($"{Path.GetRelativePath(root, file)}: key \"{name}\"");
                }
            }
        }

        Assert.AreEqual(0, violations.Count,
            "Resx values with non-integer format specifiers (would cause FormatException at render time):\n"
            + string.Join("\n", violations));
    }
}
