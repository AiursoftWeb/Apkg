using System.Text;

namespace Aiursoft.AptClient;

using Abstractions;

public static class DebianPackageParser
{
    private const int BufferSize = 1024 * 4;

    public static IEnumerable<Dictionary<string, string>> Parse(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: BufferSize, leaveOpen: true);

        string? line;
        Dictionary<string, string>? currentPackage = null;
        string? currentKey = null;
        var currentValue = new StringBuilder();

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (currentPackage != null)
                {
                    if (currentKey != null) currentPackage[currentKey] = currentValue.ToString().Trim();
                    yield return currentPackage;
                    currentPackage = null;
                    currentKey = null;
                    currentValue.Clear();
                }
                continue;
            }

            if (line.StartsWith(" ") || line.StartsWith("\t"))
            {
                if (currentKey != null) currentValue.Append('\n').Append(line);
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex > 0)
            {
                if (currentPackage != null && currentKey != null) currentPackage[currentKey] = currentValue.ToString().Trim();
                if (currentPackage == null) currentPackage = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                currentKey = line.Substring(0, separatorIndex).Trim();
                currentValue.Clear();
                currentValue.Append(line.Substring(separatorIndex + 1));
            }
        }

        if (currentPackage != null)
        {
            if (currentKey != null) currentPackage[currentKey] = currentValue.ToString().Trim();
            yield return currentPackage;
        }
    }

    public static Dictionary<string, string> ParseInRelease(string content)
    {
        // InRelease is just a signed Debian control file (similar format)
        // We wrap it in a stream to reuse the parser
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));

        // The parser returns a list of "paragraphs". InRelease usually has one main paragraph with checksums.
        var paragraphs = Parse(ms).ToList();
        var mainParagraph = paragraphs.FirstOrDefault(p => p.ContainsKey("SHA256"));

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (mainParagraph == null) return result;

        var sha256Lines = mainParagraph["SHA256"].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in sha256Lines)
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                var hash = parts[0];
                var path = parts[2];
                result[path] = hash;
            }
        }
        return result;
    }

    public static DebianPackage MapToPackage(Dictionary<string, string> dict, string suite, string component)
    {
        string GetReq(string key) => dict.TryGetValue(key, out var v) ? v : throw new InvalidDataException($"Missing required field '{key}'");
        string? GetOpt(string key) => dict.TryGetValue(key, out var v) ? v : null;

        var pkg = new DebianPackage
        {
            OriginSuite = suite,
            OriginComponent = component,
            Package = GetReq("Package"),
            Version = GetReq("Version"),
            Architecture = GetReq("Architecture"),
            Maintainer = GetReq("Maintainer"),
            Description = GetReq("Description"),
            DescriptionMd5 = GetReq("Description-md5"),
            Section = GetReq("Section"),
            Priority = GetReq("Priority"),
            Origin = GetOpt("Origin") ?? string.Empty, // Often missing in PPAs
            Bugs = GetOpt("Bugs") ?? string.Empty, // Often missing

            Filename = GetReq("Filename"),
            Size = GetReq("Size"),
            MD5sum = GetOpt("MD5sum") ?? "",
            SHA1 = GetOpt("SHA1") ?? "",
            SHA256 = GetReq("SHA256"), // We verify files using SHA256 so this should be present, usually.
            SHA512 = GetOpt("SHA512") ?? "",
            InstalledSize = GetOpt("Installed-Size"),
            OriginalMaintainer = GetOpt("Original-Maintainer"),
            Homepage = GetOpt("Homepage"),
            Depends = GetOpt("Depends"),
            Source = GetOpt("Source"),
            MultiArch = GetOpt("Multi-Arch"),
            Provides = GetOpt("Provides"),
            Suggests = GetOpt("Suggests"),
            Recommends = GetOpt("Recommends"),
            Conflicts = GetOpt("Conflicts"),
            Breaks = GetOpt("Breaks"),
            Replaces = GetOpt("Replaces")
        };

        // Remove mapped keys to store the rest in Extras
        var knownKeys = new[] { "Package", "Version", "Architecture", "Maintainer", "Description", "Description-md5",
                                "Section", "Priority", "Origin", "Bugs", "Filename", "Size", "MD5sum", "SHA1", "SHA256", "SHA512",
                                "Installed-Size", "Original-Maintainer", "Homepage", "Depends", "Source", "Multi-Arch",
                                "Provides", "Suggests", "Recommends", "Conflicts", "Breaks", "Replaces" };

        foreach (var key in knownKeys) dict.Remove(key);
        foreach (var kvp in dict) pkg.Extras[kvp.Key] = kvp.Value;

        return pkg;
    }
}
