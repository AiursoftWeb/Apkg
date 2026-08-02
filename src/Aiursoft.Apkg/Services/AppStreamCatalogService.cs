using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Aiursoft.Apkg.Entities;
using Aiursoft.Apkg.Services.FileStorage;
using Aiursoft.Apkg.Sdk.Services;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;

namespace Aiursoft.Apkg.Services;

public sealed record GeneratedRepositoryFile(string RelativePath, string Sha256, long Size);

/// <summary>
/// Builds DEP-11 catalogs for explicitly declared applications in locally
/// uploaded Apkg revisions. Upstream mirrored packages are intentionally not
/// materialized or rescanned in this first implementation.
/// </summary>
public sealed class AppStreamCatalogService(
    ApkgDbContext db,
    FeatureFoldersProvider folders,
    DebResolutionService debResolution,
    GlobalSettingsService globalSettings,
    ILogger<AppStreamCatalogService> logger)
{
    public async Task<IReadOnlyList<GeneratedRepositoryFile>> GenerateAsync(
        AptRepository repository,
        int bucketId,
        IReadOnlyList<string> architectures,
        IReadOnlyList<string> components)
    {
        var allLocalPackages = await db.ApkgDebPackages
            .AsNoTracking()
            .Include(package => package.ApkgRevision).ThenInclude(revision => revision!.ApkgPackage)
            .Include(package => package.ApkgRevision).ThenInclude(revision => revision!.AppStreamApplications)
                .ThenInclude(application => application.Assets)
            .Where(package => package.RepositoryId == repository.Id && package.IsEnabled)
            .ToListAsync();
        var winners = debResolution.ResolveWinningDebs(allLocalPackages)
            .Where(package => package.ApkgRevision?.AppStreamApplications.Count > 0)
            .ToList();

        var hasScreenshots = winners.Any(package =>
            package.ApkgRevision!.AppStreamApplications.Any(application => application.Assets.Count > 0));
        var publicBaseUrl = await globalSettings.GetConfiguredPublicAptBaseUrlAsync();
        if (hasScreenshots && string.IsNullOrWhiteSpace(publicBaseUrl))
            throw new InvalidOperationException(
                "AppStream screenshots are present, but PublicAptServerDomain is not configured. " +
                "Set it to the public packages server before publishing the repository catalog.");

        var mediaBaseUrl = publicBaseUrl == null
            ? null
            : $"{publicBaseUrl}/artifacts/{repository.Distro}/media/{repository.Suite}";
        var bucketRoot = Path.Combine(folders.GetBucketsFolder(), bucketId.ToString());
        var tempRoot = Path.Combine(folders.GetWorkspaceFolder(), $"appstream-catalog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var extractedRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var generated = new List<GeneratedRepositoryFile>();
        try
        {
            foreach (var component in components)
            {
                var icon48 = new Dictionary<string, string>(StringComparer.Ordinal);
                var icon64 = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var architecture in architectures)
                {
                    var catalog = CreateCatalogRoot(repository, component);
                    var packages = winners.Where(package =>
                            string.Equals(package.ApkgRevision!.ApkgPackage!.Component, component, StringComparison.Ordinal) &&
                            (string.Equals(package.Architecture, architecture, StringComparison.Ordinal) ||
                             string.Equals(package.Architecture, "all", StringComparison.Ordinal)))
                        .OrderBy(package => package.Package, StringComparer.Ordinal)
                        .ToList();

                    foreach (var package in packages)
                    {
                        var extractedRoot = await GetExtractedRootAsync(package, tempRoot, extractedRoots);
                        foreach (var application in package.ApkgRevision!.AppStreamApplications
                                     .OrderBy(application => application.ComponentId, StringComparer.Ordinal))
                        {
                            var componentElement = await LoadComponentAsync(extractedRoot, package, application);
                            AddRepositoryData(componentElement, package, application, mediaBaseUrl);
                            await AddCachedIconsAsync(
                                componentElement, extractedRoot, package.Package,
                                application, tempRoot, icon48, icon64);
                            catalog.Root!.Add(componentElement);
                        }
                    }

                    generated.AddRange(await WriteCatalogAsync(
                        catalog, bucketRoot, component, architecture, tempRoot, mediaBaseUrl));
                }

                generated.AddRange(await WriteIconArchivesAsync(bucketRoot, component, 48, icon48));
                generated.AddRange(await WriteIconArchivesAsync(bucketRoot, component, 64, icon64));
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }

        logger.LogInformation(
            "Generated {Count} AppStream repository file(s) for {Repository} from {Packages} local package(s).",
            generated.Count, repository.Name, winners.Count);
        return generated;
    }

    private static XDocument CreateCatalogRoot(
        AptRepository repository,
        string component)
    {
        var root = new XElement("components",
            new XAttribute("version", "1.0"),
            new XAttribute("origin", $"{repository.Distro}-{repository.Suite}-{component}"));
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private async Task<string> GetExtractedRootAsync(
        ApkgDebPackage package,
        string tempRoot,
        Dictionary<string, string> extractedRoots)
    {
        if (extractedRoots.TryGetValue(package.SHA256, out var existing))
            return existing;
        var source = Path.Combine(
            folders.GetObjectsFolder(), package.SHA256[..2], $"{package.SHA256}.deb");
        if (!File.Exists(source))
            throw new FileNotFoundException(
                $"Local AppStream package object is missing: {source}");
        var destination = Path.Combine(tempRoot, "debs", package.SHA256);
        Directory.CreateDirectory(destination);
        await RunProcessAsync("dpkg-deb", ["-x", source, destination]);
        extractedRoots[package.SHA256] = destination;
        return destination;
    }

    private static async Task<XElement> LoadComponentAsync(
        string extractedRoot,
        ApkgDebPackage package,
        ApkgAppStreamApplication application)
    {
        var path = Path.Combine(
            extractedRoot,
            application.MetainfoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new InvalidDataException(
                $"Package '{package.Package}' declares AppStream component '{application.ComponentId}', " +
                $"but '{application.MetainfoPath}' is missing from the .deb.");
        await using var stream = File.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        var component = document.Root;
        if (component?.Name.LocalName != "component")
            throw new InvalidDataException($"AppStream metainfo '{path}' has no <component> root.");
        var id = component.Elements().FirstOrDefault(element => element.Name.LocalName == "id")?.Value;
        if (!string.Equals(id, application.ComponentId, StringComparison.Ordinal))
            throw new InvalidDataException(
                $"AppStream component ID '{id}' does not match declared ID '{application.ComponentId}'.");
        return new XElement(component);
    }

    private static void AddRepositoryData(
        XElement component,
        ApkgDebPackage package,
        ApkgAppStreamApplication application,
        string? mediaBaseUrl)
    {
        component.Elements().Where(element => element.Name.LocalName == "pkgname").Remove();
        component.Add(new XElement("pkgname", package.Package));

        if (application.Assets.Count == 0)
            return;
        if (mediaBaseUrl == null)
            throw new InvalidOperationException("A media base URL is required for AppStream screenshots.");

        component.Elements().Where(element => element.Name.LocalName == "screenshots").Remove();
        var screenshots = new XElement("screenshots");
        XNamespace xml = XNamespace.Xml;
        foreach (var asset in application.Assets.OrderBy(asset => asset.Order))
        {
            var screenshot = new XElement("screenshot");
            if (asset.IsDefault)
                screenshot.Add(new XAttribute("type", "default"));
            screenshot.Add(new XElement("image",
                new XAttribute("type", "source"),
                new XAttribute("width", asset.Width),
                new XAttribute("height", asset.Height),
                $"{asset.ObjectSha256}.png"));
            if (!string.IsNullOrWhiteSpace(asset.Caption))
            {
                var caption = new XElement("caption", asset.Caption);
                if (!string.Equals(asset.Locale, "C", StringComparison.Ordinal))
                    caption.Add(new XAttribute(xml + "lang", asset.Locale));
                screenshot.Add(caption);
            }
            if (!string.IsNullOrWhiteSpace(asset.Environment))
                screenshot.Add(new XAttribute("environment", asset.Environment));
            screenshots.Add(screenshot);
        }
        component.Add(screenshots);
    }

    private static async Task AddCachedIconsAsync(
        XElement component,
        string extractedRoot,
        string packageName,
        ApkgAppStreamApplication application,
        string tempRoot,
        Dictionary<string, string> icon48,
        Dictionary<string, string> icon64)
    {
        var desktopPath = Path.Combine(extractedRoot, "usr", "share", "applications", application.DesktopId);
        if (!File.Exists(desktopPath))
            throw new InvalidDataException(
                $"AppStream desktop entry '{application.DesktopId}' is missing from the .deb.");
        var desktop = await AppStreamMetadataService.ReadDesktopEntryAsync(desktopPath);
        if (!desktop.TryGetValue("Icon", out var iconName) || string.IsNullOrWhiteSpace(iconName))
            throw new InvalidDataException(
                $"AppStream desktop entry '{application.DesktopId}' has no Icon= value.");
        iconName = NormalizeIconName(iconName);
        var source = FindIconSource(extractedRoot, iconName)
            ?? throw new InvalidDataException(
                $"Icon '{iconName}' for AppStream component '{application.ComponentId}' is missing from the .deb.");
        var catalogName = $"{packageName}_{iconName}.png";

        var output48 = Path.Combine(tempRoot, "icons", "48", catalogName);
        var output64 = Path.Combine(tempRoot, "icons", "64", catalogName);
        await RenderIconAsync(source, output48, 48);
        await RenderIconAsync(source, output64, 64);
        icon48[catalogName] = output48;
        icon64[catalogName] = output64;

        component.Elements()
            .Where(element => element.Name.LocalName == "icon" && (string?)element.Attribute("type") == "cached")
            .Remove();
        component.Add(
            new XElement("icon", new XAttribute("type", "cached"), new XAttribute("width", 48), new XAttribute("height", 48), catalogName),
            new XElement("icon", new XAttribute("type", "cached"), new XAttribute("width", 64), new XAttribute("height", 64), catalogName));
    }

    private static string? FindIconSource(string extractedRoot, string iconName)
    {
        var iconRoot = Path.Combine(extractedRoot, "usr", "share", "icons", "hicolor");
        if (!Directory.Exists(iconRoot))
            return null;
        var candidates = Directory.GetFiles(iconRoot, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), iconName, StringComparison.Ordinal))
            .Where(path => Path.GetExtension(path) is ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp")
            .ToList();
        return candidates.FirstOrDefault(path => path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
               ?? candidates.OrderByDescending(path => new FileInfo(path).Length).FirstOrDefault();
    }

    private static string NormalizeIconName(string value)
    {
        var fileName = Path.GetFileName(value);
        var extension = Path.GetExtension(fileName);
        return extension.ToLowerInvariant() is ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp"
            ? Path.GetFileNameWithoutExtension(fileName)
            : fileName;
    }

    private static async Task RenderIconAsync(string source, string destination, int size)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (source.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            await RunProcessAsync("rsvg-convert", ["-w", size.ToString(), "-h", size.ToString(), "-o", destination, source]);
            return;
        }

        using var bitmap = SKBitmap.Decode(source)
            ?? throw new InvalidDataException($"Icon '{source}' could not be decoded.");
        using var resized = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (!bitmap.ScalePixels(resized, new SKSamplingOptions(SKCubicResampler.Mitchell)))
            throw new InvalidDataException($"Icon '{source}' could not be resized.");
        using var encoded = resized.Encode(SKEncodedImageFormat.Png, 100);
        await using var output = File.Create(destination);
        encoded.SaveTo(output);
    }

    private static async Task<IReadOnlyList<GeneratedRepositoryFile>> WriteCatalogAsync(
        XDocument catalog,
        string bucketRoot,
        string component,
        string architecture,
        string tempRoot,
        string? mediaBaseUrl)
    {
        var dep11Dir = Path.Combine(bucketRoot, component, "dep11");
        Directory.CreateDirectory(dep11Dir);
        var xmlPath = Path.Combine(tempRoot, $"{component}-{architecture}.xml");
        await using (var xml = File.Create(xmlPath))
            await catalog.SaveAsync(xml, SaveOptions.None, CancellationToken.None);

        var yamlPath = Path.Combine(dep11Dir, $"Components-{architecture}.yml");
        await RunProcessAsync("appstreamcli", ["convert", "--format=yaml", xmlPath, yamlPath]);
        if (mediaBaseUrl != null)
            await AddMediaBaseUrlAsync(yamlPath, mediaBaseUrl);
        var gzipPath = yamlPath + ".gz";
        await using (var input = File.OpenRead(yamlPath))
        await using (var output = File.Create(gzipPath))
        await using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            await input.CopyToAsync(gzip);

        return
        [
            await DescribeFileAsync(bucketRoot, yamlPath),
            await DescribeFileAsync(bucketRoot, gzipPath)
        ];
    }

    private static async Task AddMediaBaseUrlAsync(string yamlPath, string mediaBaseUrl)
    {
        var lines = (await File.ReadAllLinesAsync(yamlPath)).ToList();
        var origin = lines.FindIndex(line => line.StartsWith("Origin:", StringComparison.Ordinal));
        if (origin < 0)
            throw new InvalidDataException("Converted DEP-11 catalog has no Origin header.");
        lines.Insert(origin + 1, $"MediaBaseUrl: {mediaBaseUrl}");
        await File.WriteAllLinesAsync(yamlPath, lines, new UTF8Encoding(false));
    }

    private static async Task<IReadOnlyList<GeneratedRepositoryFile>> WriteIconArchivesAsync(
        string bucketRoot,
        string component,
        int size,
        IReadOnlyDictionary<string, string> icons)
    {
        var dep11Dir = Path.Combine(bucketRoot, component, "dep11");
        Directory.CreateDirectory(dep11Dir);
        var tarPath = Path.Combine(dep11Dir, $"icons-{size}x{size}.tar");
        await using (var output = File.Create(tarPath))
        await using (var tar = new TarWriter(output, TarEntryFormat.Pax))
        {
            foreach (var (name, source) in icons.OrderBy(icon => icon.Key, StringComparer.Ordinal))
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = File.OpenRead(source),
                    ModificationTime = DateTimeOffset.UnixEpoch
                };
                await tar.WriteEntryAsync(entry);
                await entry.DataStream.DisposeAsync();
            }
        }

        var gzipPath = tarPath + ".gz";
        await using (var input = File.OpenRead(tarPath))
        await using (var output = File.Create(gzipPath))
        await using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            await input.CopyToAsync(gzip);

        return
        [
            await DescribeFileAsync(bucketRoot, tarPath),
            await DescribeFileAsync(bucketRoot, gzipPath)
        ];
    }

    private static async Task<GeneratedRepositoryFile> DescribeFileAsync(string bucketRoot, string path)
    {
        await using var stream = File.OpenRead(path);
        var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
        var relative = Path.GetRelativePath(bucketRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return new GeneratedRepositoryFile(relative, sha256, stream.Length);
    }

    private static async Task RunProcessAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start '{fileName}'.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}: {error}\n{output}".Trim());
    }
}
