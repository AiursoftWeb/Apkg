using System.CommandLine;
using System.Formats.Tar;
using System.IO.Compression;
using System.Xml.Serialization;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.Apkg.Sdk.Services;
using Aiursoft.CommandFramework.Framework;
using Aiursoft.CommandFramework.Models;
using Aiursoft.CommandFramework.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Client.Handlers;

public class PublishHandler : ExecutableCommandHandlerBuilder
{
    protected override string Name => "publish";
    protected override string Description => "Build .deb packages and pack them into a .apkg archive for distribution.";

    private static readonly Option<string> PathOption =
        new(name: "--path", aliases: ["-p"])
        {
            Description = "Directory containing the .aosproj file (defaults to current directory).",
            DefaultValueFactory = _ => "."
        };

    private static readonly Option<string> OutputOption =
        new(name: "--output", aliases: ["-o"])
        {
            Description = "Output directory (defaults to bin/ inside the project directory).",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> DistroOption =
        new(name: "--distro")
        {
            Description = "Target Linux distribution (e.g. ubuntu). Defaults to the project TargetDistro.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> SuiteOption =
        new(name: "--suite")
        {
            Description = "Target suite/codename (e.g. jammy). Defaults to all suites declared in the project.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<string> ArchOption =
        new(name: "--arch")
        {
            Description = "Target CPU architecture (e.g. amd64). Defaults to all architectures declared in the project.",
            DefaultValueFactory = _ => string.Empty
        };

    private static readonly Option<bool> AllOption =
        new(name: "--all")
        {
            Description = "Build for all combinations of TargetSuites × TargetArchitectures declared in the project.",
            DefaultValueFactory = _ => false
        };

    private static readonly Option<bool> NoBuildOption =
        new(name: "--no-build")
        {
            Description = "Skip the build step and only pack existing .deb files from bin/.",
            DefaultValueFactory = _ => false
        };

    protected override IEnumerable<Option> GetCommandOptions() =>
    [
        PathOption,
        OutputOption,
        DistroOption,
        SuiteOption,
        ArchOption,
        AllOption,
        NoBuildOption,
    ];

    protected override async Task Execute(ParseResult context)
    {
        var verbose = context.GetValue(CommonOptionsProvider.VerboseOption);
        var pathArg = context.GetValue(PathOption)!;
        var outputArg = context.GetValue(OutputOption)!;
        var distroArg = context.GetValue(DistroOption)!;
        var suiteArg = context.GetValue(SuiteOption)!;
        var archArg = context.GetValue(ArchOption)!;
        var buildAll = context.GetValue(AllOption);
        var noBuild = context.GetValue(NoBuildOption);

        var services = ServiceBuilder
            .CreateCommandHostBuilder<Startup>(verbose)
            .Build()
            .Services;

        var logger = services.GetRequiredService<ILogger<PublishHandler>>();
        var aosprojSerializer = services.GetRequiredService<AosprojSerializer>();
        var debBuilder = services.GetRequiredService<DebBuilder>();
        var conditionEvaluator = services.GetRequiredService<ConditionEvaluator>();

        var projectDir = Path.GetFullPath(pathArg);
        var projectFile = AosprojSerializer.FindProjectFile(projectDir);
        var project = await aosprojSerializer.DeserializeFromFileAsync(projectFile);

        // ── Build (unless --no-build) ─────────────────────────────────────────
        List<DebBuildResult> buildResults = [];

        if (!noBuild)
        {
            // Lint first
            var linter = new AosprojLinter(conditionEvaluator);
            var issues = linter.Lint(project, projectDir);
            foreach (var issue in issues)
            {
                if (issue.Level == AosprojLinter.Severity.Error)
                    logger.LogError("[Lint/{Level}] {Message}", issue.Level, issue.Message);
                else
                    logger.LogWarning("[Lint/{Level}] {Message}", issue.Level, issue.Message);
            }
            if (issues.Any(i => i.Level == AosprojLinter.Severity.Error))
                throw new InvalidOperationException("Lint found errors. Fix them before publishing.");

            var targets = ResolveBuildTargets(project, buildAll, distroArg, suiteArg, archArg);

            logger.LogInformation("Building {Count} target(s) for {Package} {Version}...",
                targets.Count, project.PackageName, project.PackageVersion);

            var binDir = Path.Combine(projectDir, "bin");

            // Clean up stale .deb files from previous builds so they don't
            // pollute the pack step or cause the wrong version to be derived.
            if (Directory.Exists(binDir))
                foreach (var stale in Directory.GetFiles(binDir, $"{project.PackageName}_*.deb"))
                    File.Delete(stale);

            foreach (var (distro, suite, arch) in targets)
            {
                logger.LogInformation("  [{Distro}/{Suite}/{Arch}]", distro, suite, arch);
                var result = await debBuilder.BuildAsync(projectDir, project, distro, suite, arch, binDir);
                buildResults.Add(result);
            }
        }

        // ── Pack .deb files into .apkg ────────────────────────────────────────
        var binDirectory = Path.Combine(projectDir, "bin");
        var outputDir = string.IsNullOrWhiteSpace(outputArg)
            ? Path.Combine(projectDir, "bin")
            : Path.GetFullPath(outputArg);

        var entries = new List<ApkgPackageEntry>();

        if (!noBuild && buildResults.Count > 0)
        {
            logger.LogInformation("Packing {Count} .deb file(s) into .apkg:", buildResults.Count);

            foreach (var result in buildResults)
            {
                var debFileName = Path.GetFileName(result.DebPath);
                entries.Add(new ApkgPackageEntry
                {
                    DebFile = debFileName,
                    Suite = result.Suite,
                    Architecture = result.Arch
                });
                logger.LogInformation("  + {File} → {Distro}/{Suite}", debFileName, project.TargetDistro, result.Suite);
            }
        }
        else
        {
            // --no-build: scan bin/ for existing .deb files
            if (!Directory.Exists(binDirectory))
                throw new DirectoryNotFoundException(
                    $"bin/ directory not found at {binDirectory}. Run 'apkg publish' without --no-build first.");

            var allDebFiles = Directory.GetFiles(binDirectory, $"{project.PackageName}_*.deb");
            if (allDebFiles.Length == 0)
                throw new FileNotFoundException(
                    $"No .deb files found in {binDirectory} matching '{project.PackageName}_*.deb'.");

            var versionsFound = allDebFiles
                .Select(f => DeriveVersionFromDeb(f, project.PackageName))
                .Distinct()
                .ToList();
            if (versionsFound.Count > 1)
                logger.LogWarning(
                    "Found multiple versions in bin/: {Versions}. Only .deb files matching " +
                    "the project's PackageVersion should be present.",
                    string.Join(", ", versionsFound));

            logger.LogInformation("Packing {Count} .deb file(s) into .apkg:", allDebFiles.Length);

            foreach (var debPath in allDebFiles)
            {
                var debFileName = Path.GetFileName(debPath);
                var (suite, arch) = ParseDebFileName(debFileName);

                entries.Add(new ApkgPackageEntry
                {
                    DebFile = debFileName,
                    Suite = suite,
                    Architecture = arch
                });

                logger.LogInformation("  + {File} → {Distro}/{Suite}", debFileName, project.TargetDistro, suite);
            }
        }

        var manifest = new ApkgPackageManifest
        {
            Name = project.PackageName,
            Distro = project.TargetDistro,
            Component = project.Component,
            Maintainer = string.IsNullOrWhiteSpace(project.Maintainer) ? project.PackageAuthors : project.Maintainer,
            Description = project.PackageDescription,
            Homepage = project.PackageHomepage,
            License = project.LicenseType,
            RepositoryUrl = project.RepositoryUrl,
            Entries = entries
        };

        var manifestXml = SerializeManifest(manifest);

        Directory.CreateDirectory(outputDir);
        var staticVersion = DeriveStaticVersion(project.PackageVersion);
        var apkgFileName = string.IsNullOrWhiteSpace(staticVersion)
            ? $"{project.PackageName}.apkg"
            : $"{project.PackageName}.{staticVersion}.apkg";
        var apkgPath = Path.Combine(outputDir, apkgFileName);

        logger.LogInformation("Writing {File}...", apkgFileName);

        await using (var fileStream = new FileStream(apkgPath, FileMode.Create, FileAccess.Write))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false))
        {
            var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestXml);
            var manifestEntry = new PaxTarEntry(TarEntryType.RegularFile, "manifest.xml")
            {
                DataStream = new MemoryStream(manifestBytes)
            };
            await tar.WriteEntryAsync(manifestEntry);
            logger.LogDebug("  + manifest.xml");

            foreach (var entry in entries)
            {
                var debPath = Path.Combine(binDirectory, entry.DebFile);
                await tar.WriteEntryAsync(debPath, entry.DebFile);
                logger.LogDebug("  + {File}", entry.DebFile);
            }
        }

        logger.LogInformation("Done! Created {ApkgPath}", apkgPath);
    }

    internal static List<(string distro, string suite, string arch)> ResolveBuildTargets(
        AosprojProject project, bool buildAll, string distroArg, string suiteArg, string archArg)
    {
        if (buildAll || (string.IsNullOrWhiteSpace(suiteArg) && string.IsNullOrWhiteSpace(archArg)))
        {
            if (string.IsNullOrWhiteSpace(project.TargetDistro))
                throw new InvalidOperationException("Project has no <TargetDistro> declared.");
            if (project.SuiteList.Length == 0)
                throw new InvalidOperationException("Project has no <TargetSuites> declared.");
            if (project.ArchList.Length == 0)
                throw new InvalidOperationException("Project has no <TargetArchitectures> declared.");

            return (
                from suite in project.SuiteList
                from arch in project.ArchList
                select (project.TargetDistro, suite, arch)
            ).ToList();
        }

        if (string.IsNullOrWhiteSpace(suiteArg))
            throw new InvalidOperationException("Specify --suite (e.g. --suite jammy).");
        if (string.IsNullOrWhiteSpace(archArg))
            throw new InvalidOperationException("Specify --arch (e.g. --arch amd64).");

        var distro = string.IsNullOrWhiteSpace(distroArg)
            ? (string.IsNullOrWhiteSpace(project.TargetDistro) ? "ubuntu" : project.TargetDistro)
            : distroArg;

        return [(distro, suiteArg, archArg)];
    }

    internal static string DeriveVersionFromDeb(string debPath, string packageName)
    {
        var fileName = Path.GetFileNameWithoutExtension(debPath);
        // Format: {name}_{version}_{suite}_{arch} — version is between name and the last two _
        var prefix = $"{packageName}_";
        var rest = fileName[prefix.Length..];
        var lastUnderscore = rest.LastIndexOf('_');
        var middle = rest[..lastUnderscore];
        var secondLastUnderscore = middle.LastIndexOf('_');
        return middle[..secondLastUnderscore];
    }

    /// <summary>
    /// Derives a static, filesystem-safe version string from a PackageVersion template.
    /// Template variables like $(SuiteShortName) are stripped so that e.g.
    /// "1.0.3+$(SuiteShortName)1" becomes "1.0.3" and "1.0.2" stays "1.0.2".
    /// </summary>
    internal static string DeriveStaticVersion(string packageVersion)
    {
        if (string.IsNullOrWhiteSpace(packageVersion))
            return string.Empty;

        var dollarIdx = packageVersion.IndexOf("$(", StringComparison.Ordinal);
        if (dollarIdx < 0)
            return packageVersion.Trim();

        // Trim any trailing separator characters left before the template variable
        return packageVersion[..dollarIdx].TrimEnd('+', '-', '~', '.');
    }

    internal static (string suite, string arch) ParseDebFileName(string fileName)
    {
        // Format: {name}_{version}_{suite}_{arch}.deb — parse from the right
        var name = Path.GetFileName(fileName);
        if (name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        var lastUnderscore = name.LastIndexOf('_');
        if (lastUnderscore < 0)
            throw new InvalidOperationException(
                $"Cannot parse suite/arch from deb filename '{fileName}'. Expected format: <name>_<version>_<suite>_<arch>.deb");

        var arch = name[(lastUnderscore + 1)..];
        var rest = name[..lastUnderscore];
        var secondLastUnderscore = rest.LastIndexOf('_');
        if (secondLastUnderscore < 0)
            throw new InvalidOperationException(
                $"Cannot parse suite/arch from deb filename '{fileName}'. Expected format: <name>_<version>_<suite>_<arch>.deb");

        var suite = rest[(secondLastUnderscore + 1)..];
        return (suite, arch);
    }

    private static string SerializeManifest(ApkgPackageManifest manifest)
    {
        var serializer = new XmlSerializer(typeof(ApkgPackageManifest));
        using var sw = new StringWriter();
        serializer.Serialize(sw, manifest);
        return sw.ToString();
    }
}
