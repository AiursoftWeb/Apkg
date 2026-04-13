using System.Text.RegularExpressions;

namespace Aiursoft.AptClient;

public class AptSourceExtractor
{
    public static List<AptPackageSource> ExtractSources(string fileContent, string targetArch, Func<HttpClient>? httpClientFactory = null)
    {
        // Simple heuristic: if it contains "Types:", it's likely deb822.
        // If lines start with "deb ", it's legacy.
        // A robust way uses the first non-comment line.
        var firstLine = fileContent.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"));

        if (firstLine != null && (firstLine.StartsWith("Types:", StringComparison.OrdinalIgnoreCase) || fileContent.Contains("Types:")))
        {
            return ExtractDeb822Sources(fileContent, targetArch, httpClientFactory);
        }
        else
        {
            return ExtractLegacySources(fileContent, targetArch, httpClientFactory);
        }
    }

    private static List<AptPackageSource> ExtractDeb822Sources(string fileContent, string targetArch, Func<HttpClient>? httpClientFactory)
    {
        var sources = new List<AptPackageSource>();
        var repoCache = new Dictionary<string, AptRepository>();

        // Split by double newline to separate stanzas
        // Normalize line endings first
        var normalized = fileContent.Replace("\r\n", "\n");
        var blocks = Regex.Split(normalized, @"\n\n+");

        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;

            var dict = ParseDeb822Block(block);

            // Filter Types: deb
            if (dict.TryGetValue("Types", out var type) && !type.Contains("deb")) continue;

            if (!dict.TryGetValue("URIs", out var urisLine)) continue;
            if (!dict.TryGetValue("Suites", out var suitesLine)) continue;
            if (!dict.TryGetValue("Components", out var componentsLine)) continue;

            string? signedBy = null;
            if (dict.TryGetValue("Signed-By", out var sb))
            {
                signedBy = HandleSignedBy(sb);
            }

            var uriList = urisLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var suiteList = suitesLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var componentList = componentsLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var uri in uriList)
            {
                foreach (var suite in suiteList)
                {
                    // Identify Repository Key
                    var repoKey = $"{uri}|{suite}";
                    if (!repoCache.TryGetValue(repoKey, out var repo))
                    {
                        repo = new AptRepository(uri, suite, signedBy, httpClientFactory);
                        repoCache[repoKey] = repo;
                    }

                    foreach (var component in componentList)
                    {
                        sources.Add(new AptPackageSource(repo, component, targetArch, httpClientFactory));
                    }
                }
            }
        }
        return sources;
    }

    private static List<AptPackageSource> ExtractLegacySources(string fileContent, string targetArch, Func<HttpClient>? httpClientFactory)
    {
        var sources = new List<AptPackageSource>();
        var repoCache = new Dictionary<string, AptRepository>();

        var lines = fileContent.Split('\n');
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

            // Format: deb [options] uri suite component1 component2 ...
            // Options are optional, enclosed in []

            if (!trimmed.StartsWith("deb ")) continue; // We only support binary 'deb' for now, not 'deb-src'

            var rest = trimmed.Substring(4).Trim();
            string? signedBy = null;

            // Check for options [key=value key2=value2]
            if (rest.StartsWith("["))
            {
                var endBracket = rest.IndexOf(']');
                if (endBracket > 0)
                {
                    var optionsContent = rest.Substring(1, endBracket - 1);
                    var options = optionsContent.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var opt in options)
                    {
                        var kvp = opt.Split(new[] { '=' }, 2);
                        if (kvp.Length == 2 && kvp[0].Trim().ToLowerInvariant() == "signed-by")
                        {
                            signedBy = kvp[1].Trim();
                        }
                    }
                    rest = rest.Substring(endBracket + 1).Trim();
                }
            }

            // Now parse URI Suite Component...
            var parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue; // Must have uri, suite, component

            var uri = parts[0];
            var suite = parts[1];
            var components = parts.Skip(2);

            var repoKey = $"{uri}|{suite}";
            if (!repoCache.TryGetValue(repoKey, out var repo))
            {
                repo = new AptRepository(uri, suite, signedBy, httpClientFactory);
                repoCache[repoKey] = repo;
            }

            foreach (var component in components)
            {
                sources.Add(new AptPackageSource(repo, component, targetArch, httpClientFactory));
            }
        }

        return sources;
    }

    private static string? HandleSignedBy(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        content = content.Trim();

        // If it looks like a PGP block (starts with header or contains newlines that suggest a block), save to file
        if (content.Contains("-----BEGIN PGP PUBLIC KEY BLOCK-----") || content.Contains("\n"))
        {
            var tempAsc = Path.GetTempFileName();
            var tempGpg = tempAsc + ".gpg"; // Target binary path

            // Handle the dot '.' indent convention in deb822
            // Deb822 multiline values usually start with space. Empty lines are " .".
            // We strip the leading space if we parsed it manually, but ParseDeb822Block might have kept it?
            // ParseDeb822Block joins lines with \n.
            // If the original file had " .", ParseDeb822Block (simple one) might capture it as ".".
            // Let's being robust:
            var lines = content.Split('\n');
            var cleanLines = lines.Select(l =>
            {
                var trimmed = l.Trim();
                if (trimmed == ".") return ""; // Convert " ." to empty line
                return l; // Keep original indentation (PGP handles it, or should we trim?)
                // Actually PGP blocks inside deb822 often have 1 space indent.
                // gpg --dearmor is quite standardcompliant, it ignores non-base64 chars?
                // Best to trim start?
                // Let's try to keeping it simple: just handle the dot.
            });
            var cleanContent = string.Join("\n", cleanLines);

            File.WriteAllText(tempAsc, cleanContent);

            // Try to de-armor using gpg
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gpg",
                    Arguments = $"--dearmor --batch --yes -o \"{tempGpg}\" \"{tempAsc}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(5000); // 5 seconds timeout

                    if (p.ExitCode == 0 && File.Exists(tempGpg))
                    {
                        // Success!
                        try { File.Delete(tempAsc); } catch (Exception) { /* Ignore */ }
                        return tempGpg;
                    }
                    else
                    {
                        // Logging failure here might be hard as we are in static method.
                        // But we can fallback.
                        // Console.WriteLine($"GPG Dearmor failed: {stderr}");
                    }
                }
            }
            catch (Exception)
            {
                // gpg tool missing or execution failed
            }

            // Fallback: Just return the ASCII file (maybe verified supports it or user has weird setup)
            // Rename to .asc for clarity? Or valid pgp extension?
            // Some systems allow .asc.
            // We move tempAsc to something persistent because tempAsc is named /tmp/tmpXXXX.tmp
            var fallbackPath = tempAsc + ".gpg";
            // Check if gpg failed but file exists? No.
            if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
            File.Move(tempAsc, fallbackPath);
            return fallbackPath;
        }

        return content;
    }

    private static Dictionary<string, string> ParseDeb822Block(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // This is a simplified debounce parser.
        // Real deb822 supports folding.
        // Field: value
        //  continued value

        string? currentKey = null;
        var currentValue = "";

        using var reader = new StringReader(block);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith(" ") || line.StartsWith("\t"))
            {
                // Continuation
                if (currentKey != null)
                {
                    // Deb822 continuation preserves newlines typically, but usually we just append.
                    // For Signed-By, we want to preserve structure.
                    currentValue += "\n" + line.TrimStart();
                }
            }
            else
            {
                // New Field
                if (currentKey != null)
                {
                    result[currentKey] = currentValue;
                }

                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    currentKey = parts[0].Trim();
                    currentValue = parts[1].Trim();
                }
            }
        }

        if (currentKey != null)
        {
            result[currentKey] = currentValue;
        }

        return result;
    }
}
