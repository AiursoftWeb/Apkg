using System.Text;
using System.Text.RegularExpressions;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.AptClient;

namespace Aiursoft.Apkg.Sdk.Services;

public static class BuildInputResolver
{
    public static string[] Split(string value) => value.Split([' ', ',', '\t', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static IReadOnlyList<FetchInput> Resolve(
        AosprojProject project, string projectDir, string distro, string suite, string arch)
    {
        Require(distro, "distro");
        Require(suite, "suite");
        Require(arch, "arch");
        foreach (var target in new[] { distro, suite, arch })
            if (!Regex.IsMatch(target, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$"))
                throw new InvalidDataException($"Invalid build target '{target}'.");
        var errors = Validate(project).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors));
        var evaluator = new ConditionEvaluator();
        var context = ConditionEvaluator.BuildContext(distro, suite, arch, component: project.Component);
        string Expand(string value)
        {
            var expanded = DebBuilder.ResolveVariables(value, project, distro, suite, arch);
            if (expanded.Contains("$(", StringComparison.Ordinal))
                throw new InvalidDataException($"Unresolved build input variable in '{value}'.");
            return expanded;
        }
        var sources = new Dictionary<string, IReadOnlyList<FetchSource>>(StringComparer.Ordinal);
        foreach (var source in project.BuildSources.Where(s => evaluator.Evaluate(s.Condition, context)))
        {
            var entries = (from pocket in Split(Expand(source.Suites))
                           from component in Split(Expand(source.Components))
                           select new FetchSource(Expand(source.Url), pocket, component,
                               Path.GetFullPath(Expand(source.SignedBy), projectDir))).ToArray();
            if (!sources.TryAdd(source.Name, entries))
                throw new InvalidDataException($"Multiple active BuildSource entries named '{source.Name}'.");
        }
        var inputs = new List<FetchInput>();
        foreach (var input in project.BuildInputs.Where(i => evaluator.Evaluate(i.Condition, context)))
        {
            var selected = Split(input.Sources).SelectMany(name => sources.TryGetValue(name, out var entries)
                ? entries : throw new InvalidDataException($"BuildInput '{input.Package}' references inactive or unknown source '{name}'.")).ToArray();
            var package = Expand(input.Package);
            inputs.Add(new FetchInput(string.IsNullOrEmpty(input.Id) ? package : Expand(input.Id),
                package, Expand(input.Arch), selected));
        }
        PackageFetchService.Validate(inputs);
        return inputs;
    }

    public static IEnumerable<string> Validate(AosprojProject project)
    {
        foreach (var source in project.BuildSources)
        {
            foreach (var (name, value) in new[] { ("Name", source.Name), ("Url", source.Url),
                         ("Suites", source.Suites), ("Components", source.Components), ("SignedBy", source.SignedBy) })
                if (string.IsNullOrWhiteSpace(value)) yield return $"BuildSource requires {name}.";
            if (!Regex.IsMatch(source.Name, @"^[a-zA-Z0-9][a-zA-Z0-9_.-]*$"))
                yield return $"Invalid BuildSource Name '{source.Name}'.";
        }
        foreach (var input in project.BuildInputs)
        {
            foreach (var (name, value) in new[] { ("Include", input.Package), ("Sources", input.Sources), ("Arch", input.Arch) })
                if (string.IsNullOrWhiteSpace(value)) yield return $"BuildInput requires {name}.";
            foreach (var source in Split(input.Sources))
                if (!project.BuildSources.Any(s => s.Name == source))
                    yield return $"BuildInput '{input.Package}' references unknown source '{source}'.";
        }
    }

    // Deliberately supports an explicit subset of deb822. Unsupported policy fields fail
    // instead of being silently ignored (notably Trusted and Allow-Insecure).
    public static IReadOnlyList<FetchSource> ReadSourcesFile(string path, string arch)
    {
        path = Path.GetFullPath(path);
        var lines = File.ReadAllLines(path).Where(l => !l.TrimStart().StartsWith('#')).ToArray();
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) { fields.Clear(); continue; }
            if (char.IsWhiteSpace(line[0]))
            {
                if (fields.Count == 0) throw new InvalidDataException("Source continuation line has no field.");
                continue;
            }
            var separator = line.IndexOf(':');
            if (separator <= 0)
                throw new InvalidDataException("--sources requires a deb822 .sources file, not sources.list syntax.");
            if (!fields.Add(line[..separator].Trim()))
                throw new InvalidDataException($"Duplicate source field '{line[..separator]}'.");
        }
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        var result = new List<FetchSource>();
        foreach (var stanza in DebianPackageParser.Parse(stream))
        {
            var enabled = stanza.GetValueOrDefault("Enabled", "yes");
            if (enabled == "no") continue;
            if (enabled != "yes") throw new InvalidDataException("Enabled must be yes or no.");
            foreach (var field in stanza.Keys)
                if (!new[] { "Types", "URIs", "Suites", "Components", "Signed-By", "Architectures", "Enabled" }.Contains(field, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Unsupported source field '{field}'.");
            if (stanza.GetValueOrDefault("Types") != "deb")
                throw new InvalidDataException("Each source stanza must specify Types: deb.");
            string Field(string name)
            {
                var value = stanza.GetValueOrDefault(name, "");
                Require(value, $"source field {name}");
                return value;
            }
            if (stanza.TryGetValue("Architectures", out var arches) && !Split(arches).Contains(arch))
                throw new InvalidDataException($"Source Architectures does not include requested architecture '{arch}'.");
            var key = Field("Signed-By");
            if (key.Contains('\n') || key.Contains("-----BEGIN", StringComparison.Ordinal))
                throw new InvalidDataException("Signed-By must name a local keyring file; inline keys are not supported by fetch.");
            key = Path.GetFullPath(key, Path.GetDirectoryName(path)!);
            foreach (var url in Split(Field("URIs")))
            foreach (var suite in Split(Field("Suites")))
            foreach (var component in Split(Field("Components")))
                result.Add(new FetchSource(url, suite, component, key));
        }
        if (result.Count == 0) throw new InvalidDataException("No enabled binary sources declared.");
        return result;
    }

    public static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Explicit {name} is required.");
    }
}
