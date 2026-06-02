using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Reads and writes .aosproj files using XDocument for full flexibility
/// (handles Condition attributes, multiple PropertyGroups and ItemGroups).
/// </summary>
public class AosprojSerializer
{
    // ── Deserialize ──────────────────────────────────────────────────────────

    public Task<AosprojProject> DeserializeFromFileAsync(string path)
    {
        var xml = XDocument.Load(path);
        return Task.FromResult(Deserialize(xml));
    }

    public AosprojProject Deserialize(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("Empty .aosproj file.");
        var project = new AosprojProject();

        // Collect all PropertyGroup elements
        foreach (var pg in root.Elements("PropertyGroup"))
            ReadPropertyGroup(pg, project);

        // Collect all ItemGroup elements
        foreach (var ig in root.Elements("ItemGroup"))
            ReadItemGroup(ig, project);

        return project;
    }

    private static void ReadPropertyGroup(XElement pg, AosprojProject project)
    {
        foreach (var el in pg.Elements())
        {
            switch (el.Name.LocalName)
            {
                case "PackageName":        project.PackageName = el.Value; break;
                case "PackageVersion":     project.PackageVersion = el.Value; break;
                case "PackageDescription": project.PackageDescription = el.Value; break;
                case "PackageAuthors":     project.PackageAuthors = el.Value; break;
                case "Maintainer":         project.Maintainer = el.Value; break;
                case "PackageHomepage":    project.PackageHomepage = el.Value; break;
                case "RepositoryUrl":      project.RepositoryUrl = el.Value; break;
                case "LicenseType":        project.LicenseType = el.Value; break;
                case "LicenseFile":        project.LicenseFile = el.Value; break;
                case "PackageTags":        project.PackageTags = el.Value; break;
                case "Provides":           project.Provides = el.Value; break;
                case "Conflicts":          project.Conflicts = el.Value; break;
                case "Replaces":           project.Replaces = el.Value; break;
                case "Breaks":             project.Breaks = el.Value; break;
                case "Section":            project.Section = el.Value; break;
                case "Priority":           project.Priority = el.Value; break;
                case "Component":          project.Component = el.Value; break;
                case "TargetDistro":       project.TargetDistro = el.Value; break;
                case "TargetSuites":       project.TargetSuites = el.Value; break;
                case "TargetArchitectures": project.TargetArchitectures = el.Value; break;
                case "UpstreamUrl":        project.UpstreamUrl = el.Value; break;
                case "UpstreamDistro":     project.UpstreamDistro = el.Value; break;
                case "UpstreamPackage":    project.UpstreamPackage = el.Value; break;
                case "UpstreamSuite":      project.UpstreamSuite = el.Value; break;
                case "UpstreamComponent":  project.UpstreamComponent = el.Value; break;
                case "UpstreamArch":       project.UpstreamArch = el.Value; break;
                case "UpstreamSuiteMapping": project.UpstreamSuiteMapping = el.Value; break;
                case "DependencyCheckUrl":    project.DependencyCheckUrl = el.Value; break;
                case "DependencyCheckSuiteMap": project.DependencyCheckSuiteMap = el.Value; break;
            }
        }
    }

    private static void ReadItemGroup(XElement ig, AosprojProject project)
    {
        foreach (var el in ig.Elements())
        {
            var condition = (string?)el.Attribute("Condition");
            switch (el.Name.LocalName)
            {
                case "PrebuildCommand":
                    project.PrebuildCommands.Add(new PrebuildCommandItem
                    {
                        Run = (string?)el.Attribute("Run") ?? el.Value,
                        Condition = condition
                    });
                    break;
                case "IncludeFile":
                    project.IncludeFiles.Add(new IncludeFileItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? string.Empty,
                        Target = (string?)el.Attribute("Target") ?? string.Empty,
                        Condition = condition
                    });
                    break;
                case "IncludeFolder":
                    project.IncludeFolders.Add(new IncludeFolderItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? string.Empty,
                        Target = (string?)el.Attribute("Target") ?? string.Empty,
                        Condition = condition
                    });
                    break;
                case "IncludeScript":
                    project.IncludeScripts.Add(new IncludeScriptItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? string.Empty,
                        Target = (string?)el.Attribute("Target") ?? string.Empty,
                        Condition = condition
                    });
                    break;
                case "ConfFile":
                    project.ConfFiles.Add(new ConfFileItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? string.Empty,
                        Target = (string?)el.Attribute("Target") ?? string.Empty,
                        Condition = condition
                    });
                    break;
                case "Dependency":
                    var depValue = (string?)el.Attribute("Include") ?? el.Value;
                    project.Dependencies.Add(new ConditionalValue
                    {
                        Condition = condition,
                        Value = depValue
                    });
                    break;
                case "Recommend":
                    var recValue = (string?)el.Attribute("Include") ?? el.Value;
                    project.Recommends.Add(new ConditionalValue
                    {
                        Condition = condition,
                        Value = recValue
                    });
                    break;
                case "Suggest":
                    var sugValue = (string?)el.Attribute("Include") ?? el.Value;
                    project.Suggests.Add(new ConditionalValue
                    {
                        Condition = condition,
                        Value = sugValue
                    });
                    break;
                case "PostInstallScript":
                    project.PostInstallScripts.Add(new PostInstallScriptItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? el.Value,
                        Condition = condition
                    });
                    break;
                case "PreRemoveScript":
                    project.PreRemoveScripts.Add(new PreRemoveScriptItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? el.Value,
                        Condition = condition
                    });
                    break;
                case "SystemdUnit":
                    var autoEnableAttr = (string?)el.Attribute("AutoEnable");
                    project.SystemdUnits.Add(new SystemdUnitItem
                    {
                        Source = (string?)el.Attribute("Include")  ?? el.Value,
                        Condition = condition,
                        AutoEnable = autoEnableAttr == null || bool.Parse(autoEnableAttr)
                    });
                    break;
            }
        }
    }

    // ── Serialize ────────────────────────────────────────────────────────────

    public Task SerializeToFileAsync(AosprojProject project, string path)
    {
        var doc = Serialize(project);
        doc.Save(path);
        return Task.CompletedTask;
    }

    public XDocument Serialize(AosprojProject project)
    {
        var pg = new XElement("PropertyGroup",
            Elem("PackageName", project.PackageName),
            Elem("PackageVersion", project.PackageVersion),
            Elem("PackageDescription", project.PackageDescription),
            Elem("PackageAuthors", project.PackageAuthors),
            Elem("Maintainer", project.Maintainer),
            Elem("PackageHomepage", project.PackageHomepage),
            Elem("RepositoryUrl", project.RepositoryUrl),
            Elem("LicenseType", project.LicenseType),
            Elem("LicenseFile", project.LicenseFile),
            Elem("PackageTags", project.PackageTags),
            Elem("Provides", project.Provides),
            Elem("Conflicts", project.Conflicts),
            Elem("Replaces", project.Replaces),
            Elem("Breaks", project.Breaks),
            Elem("Component", project.Component),
            Elem("Section", project.Section),
            Elem("Priority", project.Priority),
            Elem("TargetDistro", project.TargetDistro),
            Elem("TargetSuites", project.TargetSuites),
            Elem("TargetArchitectures", project.TargetArchitectures),
            Elem("UpstreamUrl", project.UpstreamUrl),
            Elem("UpstreamDistro", project.UpstreamDistro),
            Elem("UpstreamPackage", project.UpstreamPackage),
            Elem("UpstreamSuite", project.UpstreamSuite),
            Elem("UpstreamComponent", project.UpstreamComponent),
            Elem("UpstreamArch", project.UpstreamArch),
            Elem("UpstreamSuiteMapping", project.UpstreamSuiteMapping),
            Elem("DependencyCheckUrl", project.DependencyCheckUrl),
            Elem("DependencyCheckSuiteMap", project.DependencyCheckSuiteMap)
        );

        var itemGroups = new List<XElement>();

        if (project.PrebuildCommands.Count > 0)
        {
            itemGroups.Add(new XElement("ItemGroup",
                project.PrebuildCommands.Select(c => ItemElem("PrebuildCommand", c.Condition,
                    new XAttribute("Run", c.Run)))));
        }

        var fileItems = new List<object>();
        fileItems.AddRange(project.IncludeFiles.Select(f =>
            (object)ItemElem("IncludeFile", f.Condition,
                new XAttribute("Include", f.Source),
                new XAttribute("Target", f.Target))));
        fileItems.AddRange(project.IncludeFolders.Select(f =>
            (object)ItemElem("IncludeFolder", f.Condition,
                new XAttribute("Include", f.Source),
                new XAttribute("Target", f.Target))));
        fileItems.AddRange(project.IncludeScripts.Select(f =>
            (object)ItemElem("IncludeScript", f.Condition,
                new XAttribute("Include", f.Source),
                new XAttribute("Target", f.Target))));
        fileItems.AddRange(project.ConfFiles.Select(f =>
            (object)ItemElem("ConfFile", f.Condition,
                new XAttribute("Include", f.Source),
                new XAttribute("Target", f.Target))));

        if (fileItems.Count > 0)
            itemGroups.Add(new XElement("ItemGroup", fileItems));

        var depItems = project.Dependencies.Select(d =>
            (object)ItemElem("Dependency", d.Condition,
                new XAttribute("Include", d.Value)));
        if (project.Dependencies.Count > 0)
            itemGroups.Add(new XElement("ItemGroup", depItems));

        var recItems = project.Recommends.Select(r =>
            (object)ItemElem("Recommend", r.Condition,
                new XAttribute("Include", r.Value)));
        if (project.Recommends.Count > 0)
            itemGroups.Add(new XElement("ItemGroup", recItems));

        var sugItems = project.Suggests.Select(s =>
            (object)ItemElem("Suggest", s.Condition,
                new XAttribute("Include", s.Value)));
        if (project.Suggests.Count > 0)
            itemGroups.Add(new XElement("ItemGroup", sugItems));

        var scriptItems = new List<object>();
        scriptItems.AddRange(project.PostInstallScripts.Select(s =>
            (object)ItemElem("PostInstallScript", s.Condition,
                new XAttribute("Include", s.Source))));
        scriptItems.AddRange(project.PreRemoveScripts.Select(s =>
            (object)ItemElem("PreRemoveScript", s.Condition,
                new XAttribute("Include", s.Source))));
        scriptItems.AddRange(project.SystemdUnits.Select(u =>
            (object)ItemElem("SystemdUnit", u.Condition,
                new XAttribute("Include", u.Source),
                new XAttribute("AutoEnable", u.AutoEnable.ToString().ToLowerInvariant()))));

        if (scriptItems.Count > 0)
            itemGroups.Add(new XElement("ItemGroup", scriptItems));

        var root = new XElement("Project",
            new XAttribute("Sdk", "Aiursoft.Apkg.Sdk"),
            pg);
        foreach (var ig in itemGroups)
            root.Add(ig);

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Returns null for empty/whitespace values so XElement silently skips them.
    private static XElement? Elem(string name, string value) =>
        string.IsNullOrWhiteSpace(value) ? null : new XElement(name, value);

    private static XElement ItemElem(string name, string? condition, params object[] attrs)
    {
        var el = new XElement(name, attrs);
        if (!string.IsNullOrWhiteSpace(condition))
            el.Add(new XAttribute("Condition", condition));
        return el;
    }

    // ── Find project file ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the .aosproj file in a directory. Throws if zero or multiple found.
    /// </summary>
    public static string FindProjectFile(string directory)
    {
        var files = Directory.GetFiles(directory, "*.aosproj");
        return files.Length switch
        {
            0 => throw new FileNotFoundException($"No .aosproj file found in {directory}"),
            1 => files[0],
            _ => throw new InvalidOperationException(
                $"Multiple .aosproj files found in {directory}. Specify one explicitly.")
        };
    }
}
