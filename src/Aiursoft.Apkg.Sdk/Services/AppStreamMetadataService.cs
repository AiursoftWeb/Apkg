using System.Xml.Linq;
using Aiursoft.Apkg.Sdk.Models;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Installs desktop application metadata into a Debian staging tree and creates
/// a deterministic minimal AppStream metainfo document when one was not supplied.
/// </summary>
public sealed class AppStreamMetadataService(ILogger<AppStreamMetadataService> logger)
{
    private const UnixFileMode DataFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public async Task InstallAsync(
        string projectDir,
        string stagingRoot,
        AosprojProject project,
        IEnumerable<AppStreamApplicationItem> applications)
    {
        foreach (var application in applications)
        {
            var desktopSource = ResolveSource(projectDir, application.Source);
            var iconSource = ResolveSource(projectDir, application.Icon);
            var desktopId = Path.GetFileName(desktopSource);
            var componentId = GetComponentId(application);

            CopyDataFile(
                desktopSource,
                Path.Combine(stagingRoot, "usr", "share", "applications", desktopId));

            var iconDirectory = string.Equals(Path.GetExtension(iconSource), ".svg", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(stagingRoot, "usr", "share", "icons", "hicolor", "scalable", "apps")
                : Path.Combine(stagingRoot, "usr", "share", "icons", "hicolor", "256x256", "apps");
            CopyDataFile(iconSource, Path.Combine(iconDirectory, Path.GetFileName(iconSource)));

            var metainfoDestination = Path.Combine(
                stagingRoot, "usr", "share", "metainfo", $"{componentId}.metainfo.xml");
            Directory.CreateDirectory(Path.GetDirectoryName(metainfoDestination)!);

            if (!string.IsNullOrWhiteSpace(application.Metainfo))
            {
                CopyDataFile(ResolveSource(projectDir, application.Metainfo), metainfoDestination);
            }
            else
            {
                var desktop = await ReadDesktopEntryAsync(desktopSource);
                var metainfo = GenerateMetainfo(project, componentId, desktopId, desktop);
                await using var stream = File.Create(metainfoDestination);
                await metainfo.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
                SetDataFileMode(metainfoDestination);
            }

            logger.LogDebug("  + /usr/share/metainfo/{ComponentId}.metainfo.xml [AppStream]", componentId);
        }
    }

    public static string GetComponentId(AppStreamApplicationItem application)
    {
        var desktopId = Path.GetFileName(application.Source);
        return desktopId.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase)
            ? desktopId[..^".desktop".Length]
            : Path.GetFileNameWithoutExtension(desktopId);
    }

    public static async Task<IReadOnlyDictionary<string, string>> ReadDesktopEntryAsync(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var inDesktopEntry = false;
        foreach (var rawLine in await File.ReadAllLinesAsync(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inDesktopEntry = string.Equals(line, "[Desktop Entry]", StringComparison.Ordinal);
                continue;
            }
            if (!inDesktopEntry)
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    public static XDocument GenerateMetainfo(
        AosprojProject project,
        string componentId,
        string desktopId,
        IReadOnlyDictionary<string, string> desktop)
    {
        XNamespace xml = XNamespace.Xml;
        var name = desktop.GetValueOrDefault("Name", project.PackageName);
        var summary = desktop.GetValueOrDefault(
            "Comment",
            project.PackageDescription.Split('\n', StringSplitOptions.TrimEntries)[0]);
        var description = string.Join(' ', project.PackageDescription
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var component = new XElement("component",
            new XAttribute("type", "desktop-application"),
            new XElement("id", componentId),
            new XElement("metadata_license", project.AppStreamMetadataLicense),
            new XElement("project_license", project.LicenseType),
            new XElement("name", name));

        foreach (var (key, value) in desktop
                     .Where(pair => pair.Key.StartsWith("Name[", StringComparison.Ordinal) && pair.Key.EndsWith(']'))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var locale = key[5..^1].Replace('_', '-');
            component.Add(new XElement("name", new XAttribute(xml + "lang", locale), value));
        }

        component.Add(new XElement("summary", summary));

        foreach (var (key, value) in desktop
                     .Where(pair => pair.Key.StartsWith("Comment[", StringComparison.Ordinal) && pair.Key.EndsWith(']'))
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var locale = key[8..^1].Replace('_', '-');
            component.Add(new XElement("summary", new XAttribute(xml + "lang", locale), value));
        }

        component.Add(new XElement("description", new XElement("p", description)));

        var developerName = string.IsNullOrWhiteSpace(project.AppStreamDeveloperName)
            ? project.PackageAuthors
            : project.AppStreamDeveloperName;
        if (!string.IsNullOrWhiteSpace(developerName))
            component.Add(new XElement("developer_name", developerName));
        if (desktop.TryGetValue("Categories", out var categories))
        {
            var categoryElements = categories
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(category => new XElement("category", category));
            component.Add(new XElement("categories", categoryElements));
        }
        if (!string.IsNullOrWhiteSpace(project.PackageHomepage))
            component.Add(new XElement("url", new XAttribute("type", "homepage"), project.PackageHomepage));
        if (!string.IsNullOrWhiteSpace(project.RepositoryUrl))
            component.Add(new XElement("url", new XAttribute("type", "vcs-browser"), project.RepositoryUrl));
        component.Add(new XElement("launchable", new XAttribute("type", "desktop-id"), desktopId));
        if (desktop.TryGetValue("Icon", out var icon) && !string.IsNullOrWhiteSpace(icon))
            component.Add(new XElement("icon", new XAttribute("type", "stock"), icon));

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            component);
    }

    private static string ResolveSource(string projectDir, string source) =>
        Path.GetFullPath(Path.Combine(projectDir, source));

    private static void CopyDataFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
        SetDataFileMode(destination);
    }

    private static void SetDataFileMode(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, DataFileMode);
    }
}
