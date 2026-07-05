using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
using Aiursoft.AptClient;
using Aiursoft.AptClient.Abstractions;
using Microsoft.Extensions.Logging;

namespace Aiursoft.Apkg.Sdk.Services;

/// <summary>
/// Builds a .deb package from an <see cref="AosprojProject"/> for a specific
/// (distro, suite, arch) target combination.
/// </summary>
public class DebBuilder
{
    private readonly ConditionEvaluator _evaluator;
    private readonly ILogger<DebBuilder> _logger;

    public DebBuilder(ConditionEvaluator evaluator, ILogger<DebBuilder> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <summary>Default permissions for executable files (0755, rwxr-xr-x).</summary>
    private const UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    /// <summary>Default permissions for non-executable data/config files (0644, rw-r--r--).</summary>
    private const UnixFileMode DefaultFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite |
        UnixFileMode.GroupRead |
        UnixFileMode.OtherRead;

    /// <summary>
    /// Builds a .deb for the given target and writes it to <paramref name="outputDir"/>.
    /// Returns the absolute path to the produced .deb file and the resolved version.
    /// </summary>
    public async Task<DebBuildResult> BuildAsync(
        string projectDir,
        AosprojProject project,
        string distro,
        string suite,
        string arch,
        string outputDir)
    {
        // Resolve upstream suite: substitute variables, then apply mapping
        var rawUpstreamSuite = ResolveVariables(project.UpstreamSuite, project, distro, suite, arch);

        var suiteMap = project.GetUpstreamSuiteMap();
        var resolvedUpstreamSuite = suiteMap.TryGetValue(rawUpstreamSuite, out var mapped)
            ? mapped
            : rawUpstreamSuite;

        var resolvedUpstreamComponent = ResolveVariables(project.UpstreamComponent, project, distro, suite, arch);
        var resolvedUpstreamArch = ResolveVariables(project.UpstreamArch, project, distro, suite, arch);

        var ctx = ConditionEvaluator.BuildContext(
            distro, suite, arch,
            upstreamDistro: project.UpstreamDistro,
            upstreamSuite: resolvedUpstreamSuite,
            upstreamArch: resolvedUpstreamArch,
            component: project.Component);
        bool Include(string? cond) => _evaluator.Evaluate(cond, ctx);

        // ── Staging directory: obj/<suite>_<arch> ────────────────────────────
        var stagingRoot = Path.Combine(projectDir, "obj", $"{suite}_{arch}");
        if (Directory.Exists(stagingRoot))
            Directory.Delete(stagingRoot, recursive: true);
        Directory.CreateDirectory(stagingRoot);

        // Clean up stale staging dirs left over from previous suite/arch builds
        // so that PrebuildCommands that enumerate obj/* never pick up dead data.
        var objDir = Path.Combine(projectDir, "obj");
        if (Directory.Exists(objDir))
        {
            foreach (var stale in Directory.GetDirectories(objDir, "*_*"))
            {
                if (stale != stagingRoot)
                {
                    Directory.Delete(stale, recursive: true);
                }
            }
        }

        var debianDir = Path.Combine(stagingRoot, "DEBIAN");
        Directory.CreateDirectory(debianDir);

        // ── Upstream derivation (before PrebuildCommands so scripts can modify upstream files) ─
        Dictionary<string, string>? upstreamControl = null;
        string? upstreamPostinst = null;
        string? upstreamPreinst = null;
        string? upstreamPrerm = null;
        string? upstreamPostrm = null;
        var resolvedVersion = ResolvePackageVersion(
            project.PackageVersion, suite, project.GetSuiteShortNameMap());

        if (project.HasUpstreamSource)
        {
            string? resolvedUpstreamUrl = null;
            foreach (var urlItem in project.UpstreamUrls)
            {
                if (Include(urlItem.Condition))
                {
                    resolvedUpstreamUrl = urlItem.Value;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(resolvedUpstreamUrl))
            {
                throw new InvalidOperationException($"No valid UpstreamUrl defined for architecture '{arch}' and suite '{suite}'.");
            }
            resolvedUpstreamUrl = ResolveVariables(resolvedUpstreamUrl, project, distro, suite, arch).TrimEnd('/');

            var upstreamDebPath = await DownloadUpstreamDebAsync(
                project, resolvedUpstreamUrl, resolvedUpstreamSuite, resolvedUpstreamComponent, resolvedUpstreamArch, projectDir);
            try
            {
                var upstreamExtractDir = Path.Combine(projectDir, "obj", $"_upstream_{suite}_{arch}");
                if (Directory.Exists(upstreamExtractDir))
                    Directory.Delete(upstreamExtractDir, recursive: true);

                // Extract upstream data into staging root first
                await RunCommandAsync("dpkg-deb", ["-x", upstreamDebPath, stagingRoot], projectDir);

                // Extract upstream control files to a temp location
                var upstreamDebianDir = Path.Combine(upstreamExtractDir, "DEBIAN");
                Directory.CreateDirectory(upstreamDebianDir);
                await RunCommandAsync("dpkg-deb", ["-e", upstreamDebPath, upstreamDebianDir], projectDir);

                // Parse upstream control
                var upstreamControlPath = Path.Combine(upstreamDebianDir, "control");
                if (File.Exists(upstreamControlPath))
                    upstreamControl = ParseControlFile(await File.ReadAllTextAsync(upstreamControlPath));

                // Read upstream maintainer scripts
                upstreamPostinst = File.Exists(Path.Combine(upstreamDebianDir, "postinst"))
                    ? await File.ReadAllTextAsync(Path.Combine(upstreamDebianDir, "postinst"))
                    : null;
                upstreamPreinst = File.Exists(Path.Combine(upstreamDebianDir, "preinst"))
                    ? await File.ReadAllTextAsync(Path.Combine(upstreamDebianDir, "preinst"))
                    : null;
                upstreamPrerm = File.Exists(Path.Combine(upstreamDebianDir, "prerm"))
                    ? await File.ReadAllTextAsync(Path.Combine(upstreamDebianDir, "prerm"))
                    : null;
                upstreamPostrm = File.Exists(Path.Combine(upstreamDebianDir, "postrm"))
                    ? await File.ReadAllTextAsync(Path.Combine(upstreamDebianDir, "postrm"))
                    : null;

                var upstreamVersion = upstreamControl?.GetValueOrDefault("Version") ?? "";
                resolvedVersion = resolvedVersion.Replace("$(UpstreamVersion)", upstreamVersion);

                _logger.LogInformation("Derived from upstream {Package} {Version}",
                    upstreamControl?.GetValueOrDefault("Package"),
                    upstreamVersion);
            }
            finally
            {
                DeleteIfExists(upstreamDebPath);
                var tempDir = Path.Combine(projectDir, "obj", $"_upstream_{suite}_{arch}");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }

        // ── Prebuild commands (runs after upstream extraction so scripts can modify upstream files) ─
        foreach (var cmd in project.PrebuildCommands.Where(c => Include(c.Condition)))
        {
            _logger.LogInformation("Running prebuild command: {Cmd}", cmd.Run);
            await RunShellAsync(cmd.Run, projectDir, new Dictionary<string, string>
            {
                ["APKG_STAGE_DIR"] = stagingRoot
            });
        }

        // ── DEBIAN/control ───────────────────────────────────────────────────
        var localDepends = project.Dependencies
            .Where(d => Include(d.Condition))
            .Select(d => d.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        // Strip suppressed upstream dependencies before merging
        if (upstreamControl != null && !string.IsNullOrWhiteSpace(project.SuppressUpstreamDependencies))
        {
            var suppressNames = new HashSet<string>(
                project.SuppressUpstreamDependencies.Split(' ', ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            if (upstreamControl.TryGetValue("Depends", out var upsDeps) && !string.IsNullOrWhiteSpace(upsDeps))
            {
                var filtered = upsDeps
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(d => d.Trim())
                    .Where(d => !suppressNames.Contains(d.Split(' ', 2)[0]))
                    .ToList();
                upstreamControl["Depends"] = string.Join(", ", filtered);
            }
        }

        var mergedDepends = MergeDepends(localDepends, upstreamControl);

        var localRecommends = project.Recommends
            .Where(r => Include(r.Condition))
            .Select(r => r.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        var resolvedRecommends = localRecommends.Count > 0
            ? string.Join(", ", localRecommends)
            : upstreamControl?.GetValueOrDefault("Recommends") ?? string.Empty;

        var localSuggests = project.Suggests
            .Where(s => Include(s.Condition))
            .Select(s => s.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
        var resolvedSuggests = localSuggests.Count > 0
            ? string.Join(", ", localSuggests)
            : upstreamControl?.GetValueOrDefault("Suggests") ?? string.Empty;

        var control = BuildControl(project, resolvedVersion, arch, mergedDepends, resolvedRecommends, resolvedSuggests, upstreamControl);
        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"), control);
        _logger.LogDebug("Wrote DEBIAN/control");

        // ── Copy IncludeFile items ────────────────────────────────────────────
        var confFiles = new List<string>();

        foreach (var item in project.IncludeFiles.Where(f => Include(f.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            var dest = Path.Combine(stagingRoot, NormalizeTargetPath(item.Target));
            EnsureParentDirectory(dest);
            File.Copy(src, dest, overwrite: true);
            SetFileMode(dest, item.Mode ?? DefaultFileMode);
            _logger.LogDebug("  + {Target}", item.Target);
        }

        // ── Copy IncludeScript items ───────────────────────────────────────
        foreach (var item in project.IncludeScripts.Where(f => Include(f.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            var dest = Path.Combine(stagingRoot, NormalizeTargetPath(item.Target));
            EnsureParentDirectory(dest);
            File.Copy(src, dest, overwrite: true);
            SetFileMode(dest, item.Mode ?? ExecutableMode);
            _logger.LogDebug("  + {Target} [executable]", item.Target);
        }

        // ── Copy IncludeFolder items ──────────────────────────────────────────
        foreach (var item in project.IncludeFolders.Where(f => Include(f.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            var dest = Path.Combine(stagingRoot, NormalizeTargetPath(item.Target));
            CopyDirectory(src, dest);
            _logger.LogDebug("  + {Target}/ (folder)", item.Target);
        }

        // ── Copy ConfFile items ───────────────────────────────────────────────
        foreach (var item in project.ConfFiles.Where(f => Include(f.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            var dest = Path.Combine(stagingRoot, NormalizeTargetPath(item.Target));
            EnsureParentDirectory(dest);
            File.Copy(src, dest, overwrite: true);
            SetFileMode(dest, item.Mode ?? DefaultFileMode);
            var confPath = item.Target;
            if (confPath.StartsWith("./", StringComparison.Ordinal)) confPath = confPath[1..];
            if (!confPath.StartsWith('/')) confPath = "/" + confPath;
            confFiles.Add(confPath);
            _logger.LogDebug("  + {Target} [conffile]", item.Target);
        }

        if (confFiles.Count > 0)
            await File.WriteAllTextAsync(
                Path.Combine(debianDir, "conffiles"),
                string.Join('\n', confFiles) + "\n");

        // ── DEBIAN/triggers ───────────────────────────────────────────────
        if (project.DpkgTriggers.Count > 0)
        {
            var triggersContent = new StringBuilder();
            foreach (var t in project.DpkgTriggers)
            {
                triggersContent.AppendLine($"{t.Type} {t.Name}");
            }
            var triggersPath = Path.Combine(debianDir, "triggers");
            await File.WriteAllTextAsync(triggersPath, triggersContent.ToString());
            SetFileMode(triggersPath, DefaultFileMode);
            _logger.LogDebug("  + DEBIAN/triggers ({Count} directive(s))", project.DpkgTriggers.Count);
        }

        // ── Copy SystemdUnit files ────────────────────────────────────────────
        var activeUnits = project.SystemdUnits.Where(u => Include(u.Condition)).ToList();
        var autoEnableUnits = activeUnits.Where(u => u.AutoEnable).ToList();
        foreach (var unit in activeUnits)
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, unit.Source));
            var unitName = Path.GetFileName(unit.Source);
            var unitDest = Path.Combine(stagingRoot, "lib", "systemd", "system", unitName);
            EnsureParentDirectory(unitDest);
            File.Copy(src, unitDest, overwrite: true);
            SetFileMode(unitDest, unit.Mode ?? DefaultFileMode);
            _logger.LogDebug("  + /lib/systemd/system/{Unit}", unitName);
        }

        // ── Auto-detect .service files installed via IncludeFile ──────────────
        // If a packager accidentally uses IncludeFile to ship a .service file
        // instead of the dedicated SystemdUnit element, warn and auto-promote
        // it so that postinst/prerm/postrm scripts are still generated.
        // This mirrors dh_installsystemd's auto-detection behavior.
        var systemdDirs = new[] { "/lib/systemd/system/", "/usr/lib/systemd/system/" };
        foreach (var item in project.IncludeFiles.Where(f => Include(f.Condition)))
        {
            var itemTarget = item.Target;
            if (!itemTarget.StartsWith('/'))
                itemTarget = "/" + itemTarget;
            if (!itemTarget.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!systemdDirs.Any(d => itemTarget.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                continue;

            var unitName = Path.GetFileName(itemTarget);
            // Avoid duplicate if the same unit is already declared via SystemdUnit
            if (autoEnableUnits.Any(u => Path.GetFileName(u.Source).Equals(unitName, StringComparison.OrdinalIgnoreCase)))
                continue;

            _logger.LogWarning(
                "IncludeFile '{Target}' installs a .service file but was not declared as " +
                "<SystemdUnit>. Consider using <SystemdUnit Include=\"{Source}\" AutoEnable=\"true\" /> " +
                "instead for automatic postinst/prerm/postrm generation.",
                item.Target, item.Source);

            // Synthesize an implicit SystemdUnit so the unit still gets
            // enable+start on install. UsePreset defaults to false.
            var implicitUnit = new SystemdUnitItem
            {
                Source = item.Source,
                AutoEnable = true,
                Condition = item.Condition
            };
            activeUnits.Add(implicitUnit);
            autoEnableUnits.Add(implicitUnit);
        }

        // ── PreInstallScript → DEBIAN/preinst ─────────────────────────────────
        var preinstLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPreinst = false;

        if (upstreamPreinst != null && !project.SuppressUpstreamScripts)
        {
            preinstLines.AppendLine(StripShebang(upstreamPreinst));
            hasPreinst = true;
        }

        foreach (var item in project.PreInstallScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            preinstLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPreinst = true;
        }

        if (hasPreinst)
        {
            var preinstPath = Path.Combine(debianDir, "preinst");
            await File.WriteAllTextAsync(preinstPath, preinstLines.ToString());
            SetFileMode(preinstPath, ExecutableMode);
        }

        // ── PostInstallScript → DEBIAN/postinst ───────────────────────────────
        var postinstLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPostinst = false;

        // Upstream postinst comes first (if present)
        if (upstreamPostinst != null && !project.SuppressUpstreamScripts)
        {
            postinstLines.AppendLine(StripShebang(upstreamPostinst));
            hasPostinst = true;
        }

        foreach (var item in project.PostInstallScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            postinstLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPostinst = true;
        }

        // Systemd postinst: enable+start on fresh install, try-restart on upgrade.
        // Skip when systemd is not running (e.g. Docker containers) — /run/systemd/system
        // only exists when systemd is PID 1.
        if (autoEnableUnits.Count > 0)
        {
            postinstLines.AppendLine("case \"$1\" in");
            postinstLines.AppendLine("    configure)");
            postinstLines.AppendLine("        if [ -d /run/systemd/system ]; then");
            postinstLines.AppendLine("            systemctl daemon-reload");
            postinstLines.AppendLine("            if [ -z \"$2\" ]; then");
            foreach (var unit in autoEnableUnits)
            {
                var un = Path.GetFileName(unit.Source);
                if (unit.UsePreset)
                {
                    postinstLines.AppendLine($"                systemctl preset {un}");
                }
                else
                {
                    postinstLines.AppendLine($"                systemctl enable {un}");
                }
                postinstLines.AppendLine($"                systemctl start {un} || true");
            }
            postinstLines.AppendLine("            else");
            foreach (var unit in autoEnableUnits)
                postinstLines.AppendLine($"                systemctl try-restart {Path.GetFileName(unit.Source)} || true");
            postinstLines.AppendLine("            fi");
            postinstLines.AppendLine("        fi");
            postinstLines.AppendLine("    ;;");
            postinstLines.AppendLine("esac");
            hasPostinst = true;
        }

        if (hasPostinst)
        {
            var postinstPath = Path.Combine(debianDir, "postinst");
            await File.WriteAllTextAsync(postinstPath, postinstLines.ToString());
            SetFileMode(postinstPath, ExecutableMode);
        }

        // ── PreRemoveScript → DEBIAN/prerm ────────────────────────────────────
        var prermLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPrerm = false;

        if (upstreamPrerm != null && !project.SuppressUpstreamScripts)
        {
            prermLines.AppendLine(StripShebang(upstreamPrerm));
            hasPrerm = true;
        }

        foreach (var item in project.PreRemoveScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            prermLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPrerm = true;
        }

        // Systemd prerm: stop only on remove, skip on upgrade.
        // Skip when systemd is not running (e.g. Docker containers).
        if (autoEnableUnits.Count > 0)
        {
            prermLines.AppendLine("case \"$1\" in");
            prermLines.AppendLine("    remove|deconfigure)");
            prermLines.AppendLine("        if [ -d /run/systemd/system ]; then");
            foreach (var unit in autoEnableUnits)
                prermLines.AppendLine($"            systemctl stop {Path.GetFileName(unit.Source)} || true");
            prermLines.AppendLine("        fi");
            prermLines.AppendLine("    ;;");
            prermLines.AppendLine("esac");
            hasPrerm = true;
        }

        if (hasPrerm)
        {
            var prermPath = Path.Combine(debianDir, "prerm");
            await File.WriteAllTextAsync(prermPath, prermLines.ToString());
            SetFileMode(prermPath, ExecutableMode);
        }

        // ── DEBIAN/postrm ─────────────────────────────────────────────────────
        var postrmLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPostrm = false;

        if (upstreamPostrm != null && !project.SuppressUpstreamScripts)
        {
            postrmLines.AppendLine(StripShebang(upstreamPostrm));
            hasPostrm = true;
        }

        foreach (var item in project.PostRemoveScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            postrmLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPostrm = true;
        }

        // Systemd postrm: disable on remove/purge.
        // Skip when systemd is not running (e.g. Docker containers).
        if (autoEnableUnits.Count > 0)
        {
            postrmLines.AppendLine("case \"$1\" in");
            postrmLines.AppendLine("    remove|purge)");
            postrmLines.AppendLine("        if [ -d /run/systemd/system ]; then");
            foreach (var unit in autoEnableUnits)
                postrmLines.AppendLine($"            systemctl disable {Path.GetFileName(unit.Source)} || true");
            postrmLines.AppendLine("            systemctl daemon-reload || true");
            postrmLines.AppendLine("        fi");
            postrmLines.AppendLine("    ;;");
            postrmLines.AppendLine("esac");
            hasPostrm = true;
        }

        if (hasPostrm)
        {
            var postrmPath = Path.Combine(debianDir, "postrm");
            await File.WriteAllTextAsync(postrmPath, postrmLines.ToString());
            SetFileMode(postrmPath, ExecutableMode);
        }

        // ── Compute installed-size (kibibytes) ────────────────────────────────
        var installedSizeKb = await ComputeDirectorySizeKbAsync(stagingRoot);
        // Patch control with Installed-Size
        var controlText = await File.ReadAllTextAsync(Path.Combine(debianDir, "control"));
        controlText = controlText.Replace("__INSTALLED_SIZE__", installedSizeKb.ToString());
        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"), controlText);

        // ── dpkg-deb --build ──────────────────────────────────────────────────
        Directory.CreateDirectory(outputDir);
        var debFileName = $"{project.PackageName}_{resolvedVersion}_{suite}_{arch}.deb";
        var debOutputPath = Path.Combine(outputDir, debFileName);

        _logger.LogInformation("Building {DebFile}...", debFileName);
        await RunCommandAsync("dpkg-deb", ["--build", "--root-owner-group", stagingRoot, debOutputPath], projectDir);

        _logger.LogInformation("  ✓ {DebFile}", debOutputPath);
        return new DebBuildResult
        {
            DebPath = debOutputPath,
            Version = resolvedVersion,
            Suite = suite,
            Arch = arch
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads an upstream .deb via HTTP with SHA256 verification.
    /// Returns the path to the downloaded .deb.
    /// </summary>
    private async Task<string> DownloadUpstreamDebAsync(
        AosprojProject project, string resolvedUpstreamUrl, string resolvedUpstreamSuite, string resolvedUpstreamComponent, string resolvedUpstreamArch, string projectDir)
    {
        var downloadDir = Path.Combine(projectDir, "obj");
        Directory.CreateDirectory(downloadDir);

        // Validate keyring file exists (before branching on URL type, consistent with old behavior)
        string? keyringPath = null;
        if (!string.IsNullOrWhiteSpace(project.UpstreamSignedBy))
        {
            keyringPath = Path.GetFullPath(Path.Combine(projectDir, project.UpstreamSignedBy));
            if (!File.Exists(keyringPath))
                throw new InvalidOperationException(
                    $"UpstreamSignedBy file not found: '{keyringPath}'. " +
                    "The keyring file must be committed alongside the .aosproj file.");
            _logger.LogInformation("Using GPG keyring {KeyringFile} for upstream verification.",
                project.UpstreamSignedBy);
        }

        // file:// repos: read directly from disk (HttpClient doesn't support file://)
        if (resolvedUpstreamUrl.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return await DownloadFromFileRepoAsync(
                project, resolvedUpstreamUrl, resolvedUpstreamSuite,
                resolvedUpstreamComponent, resolvedUpstreamArch, downloadDir);
        }

        // HTTPS repos
        var allowInsecure = keyringPath == null; // trust transport when no explicit keyring
        var repo = new UpstreamAptSource(resolvedUpstreamUrl, resolvedUpstreamSuite,
            signedBy: keyringPath, allowInsecure: allowInsecure);

        _logger.LogInformation("Downloading {Package} from {Url} ({Suite})...",
            project.UpstreamPackage, resolvedUpstreamUrl, resolvedUpstreamSuite);

        // Search by the upstream package's real architecture first, then fall back
        // to binary-all. When UpstreamArch=all, try binary-all first, then the
        // host's Debian arch (some mirrors omit binary-all since all packages are
        // merged into each arch-specific index per Debian convention).
        var searchArches = resolvedUpstreamArch == "all"
            ? new[] { "all", HostDebArch() }
            : new[] { resolvedUpstreamArch, "all" };

        DebianPackage? resolvedPackage = null;
        var versionComparer = new AptVersionComparisonService();
        foreach (var searchArch in searchArches)
        {
            try
            {
                var source = new AptPackageSource(repo, resolvedUpstreamComponent, searchArch);
                var candidates = new List<DebianPackage>();
                await foreach (var pkg in source.FetchPackagesAsync())
                {
                    if (string.Equals(pkg.Package.Package, project.UpstreamPackage, StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(pkg.Package);
                    }
                }

                if (candidates.Count > 0)
                {
                    candidates.Sort((a, b) => versionComparer.Compare(b.Version, a.Version));
                    var best = candidates[0];
                    if (resolvedPackage == null || versionComparer.Compare(best.Version, resolvedPackage.Version) > 0)
                        resolvedPackage = best;
                }
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                // Mirror may not carry this arch index — try the next one.
                _logger.LogDebug(ex, "Failed to fetch package index for arch={SearchArch}, trying next.", searchArch);
            }
        }

        if (resolvedPackage == null)
            throw new InvalidOperationException(
                $"Package '{project.UpstreamPackage}' not found in {resolvedUpstreamUrl} " +
                $"suite {resolvedUpstreamSuite} component {resolvedUpstreamComponent}.");

        // Download the .deb with SHA256 verification (built into AptPackageSource)
        var destPath = Path.Combine(downloadDir,
            $"{project.UpstreamPackage}_{resolvedPackage.Version}_{resolvedPackage.Architecture}.deb");
        var downloadSource = new AptPackageSource(repo, resolvedUpstreamComponent,
            resolvedPackage.Architecture);
        await downloadSource.DownloadPackageAsync(resolvedPackage, destPath);

        return destPath;
    }

    /// <summary>
    /// Downloads an upstream .deb from a local <c>file://</c> repository.
    /// Reads Packages index files directly from disk — no HTTP or GPG needed.
    /// </summary>
    private async Task<string> DownloadFromFileRepoAsync(
        AosprojProject project, string repoUrl, string suite, string component,
        string upstreamArch, string downloadDir)
    {
        var repoPath = repoUrl["file://".Length..].TrimStart('/');
        // On Unix, file:///path → /path; the TrimStart above handles both file:/// and file://
        if (!Path.IsPathRooted(repoPath))
            repoPath = "/" + repoPath;

        _logger.LogInformation("Downloading {Package} from local repo {Path} ({Suite})...",
            project.UpstreamPackage, repoPath, suite);

        var searchArches = upstreamArch == "all"
            ? new[] { "all", HostDebArch() }
            : new[] { upstreamArch, "all" };

        DebianPackage? resolvedPackage = null;
        var versionComparer = new AptVersionComparisonService();
        foreach (var searchArch in searchArches)
        {
            var binArch = $"binary-{searchArch}";
            var distsDir = Path.Combine(repoPath, "dists", suite, component, binArch);

            // Try Packages.gz first, then uncompressed Packages
            foreach (var (fileName, needsDecompress) in new[] { ("Packages.gz", true), ("Packages", false) })
            {
                var indexPath = Path.Combine(distsDir, fileName);
                if (!File.Exists(indexPath)) continue;

                try
                {
                    using var fileStream = File.OpenRead(indexPath);
                    IEnumerable<Dictionary<string, string>> dicts;
                    if (needsDecompress)
                    {
                        using var gzStream = new System.IO.Compression.GZipStream(
                            fileStream, System.IO.Compression.CompressionMode.Decompress);
                        dicts = DebianPackageParser.Parse(gzStream).ToList();
                    }
                    else
                    {
                        dicts = DebianPackageParser.Parse(fileStream).ToList();
                    }

                    var candidates = new List<DebianPackage>();
                    foreach (var dict in dicts)
                    {
                        var pkg = DebianPackageParser.MapToPackage(dict, suite, component);
                        if (string.Equals(pkg.Package, project.UpstreamPackage, StringComparison.OrdinalIgnoreCase))
                        {
                            candidates.Add(pkg);
                        }
                    }

                    if (candidates.Count > 0)
                    {
                        candidates.Sort((a, b) => versionComparer.Compare(b.Version, a.Version));
                        var best = candidates[0];
                        if (resolvedPackage == null || versionComparer.Compare(best.Version, resolvedPackage.Version) > 0)
                            resolvedPackage = best;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse {File}, trying next.", fileName);
                }

                if (resolvedPackage != null) break;
            }
        }

        if (resolvedPackage == null)
            throw new InvalidOperationException(
                $"Package '{project.UpstreamPackage}' not found in local repo {repoPath} " +
                $"suite {suite} component {component}.");

        // Copy the .deb from the local repo and verify SHA256
        var debSourcePath = Path.Combine(repoPath, resolvedPackage.Filename);
        if (!File.Exists(debSourcePath))
            throw new InvalidOperationException(
                $"Referenced .deb file not found at {debSourcePath}.");

        var destPath = Path.Combine(downloadDir,
            $"{project.UpstreamPackage}_{resolvedPackage.Version}_{resolvedPackage.Architecture}.deb");

        // Copy the file
        using (var srcStream = File.OpenRead(debSourcePath))
        using (var dstStream = File.Create(destPath))
        {
            await srcStream.CopyToAsync(dstStream);
        }

        // Verify SHA256
        var actualHash = BitConverter.ToString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(destPath)))
            .Replace("-", "").ToLowerInvariant();

        if (!string.Equals(actualHash, resolvedPackage.SHA256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destPath);
            throw new System.Security.SecurityException(
                $"SHA256 mismatch for {debSourcePath}.\nExpected: {resolvedPackage.SHA256}\nActual:   {actualHash}");
        }

        return destPath;
    }

    /// <summary>
    /// Maps .NET <see cref="RuntimeInformation.ProcessArchitecture"/> to a Debian
    /// architecture name. Used as a fallback when <c>binary-all</c> is missing
    /// from the mirror's InRelease (Ubuntu merges <c>all</c> packages into each
    /// arch-specific index, so some mirrors omit <c>binary-all</c> entirely).
    /// </summary>
    private static string HostDebArch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "armhf",
            Architecture.X86 => "i386",
            _ => "amd64"
        };
    }

    internal static string BuildControl(
        AosprojProject p, string resolvedVersion, string arch, List<string> depends,
        string recommends, string suggests,
        Dictionary<string, string>? upstreamControl = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Package: {p.PackageName}");
        sb.AppendLine($"Version: {resolvedVersion}");
        sb.AppendLine($"Architecture: {arch}");
        sb.AppendLine($"Maintainer: {(string.IsNullOrWhiteSpace(p.Maintainer) ? p.PackageAuthors : p.Maintainer)}");
        sb.AppendLine($"Installed-Size: __INSTALLED_SIZE__");
        if (depends.Count > 0)
            sb.AppendLine($"Depends: {string.Join(", ", depends)}");

        // Local fields override upstream; fall back to upstream when local is empty
        var effectiveRecommends = !string.IsNullOrWhiteSpace(recommends) ? recommends
            : upstreamControl?.GetValueOrDefault("Recommends") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effectiveRecommends))
            sb.AppendLine($"Recommends: {effectiveRecommends}");

        var effectiveSuggests = !string.IsNullOrWhiteSpace(suggests) ? suggests
            : upstreamControl?.GetValueOrDefault("Suggests") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effectiveSuggests))
            sb.AppendLine($"Suggests: {effectiveSuggests}");

        // Local fields override upstream; fall back to upstream when local is empty
        var provides = !string.IsNullOrWhiteSpace(p.Provides) ? p.Provides
            : upstreamControl?.GetValueOrDefault("Provides") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(provides))
            sb.AppendLine($"Provides: {provides}");

        var conflicts = !string.IsNullOrWhiteSpace(p.Conflicts) ? p.Conflicts
            : upstreamControl?.GetValueOrDefault("Conflicts") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(conflicts))
            sb.AppendLine($"Conflicts: {conflicts}");

        var replaces = !string.IsNullOrWhiteSpace(p.Replaces) ? p.Replaces
            : upstreamControl?.GetValueOrDefault("Replaces") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(replaces))
            sb.AppendLine($"Replaces: {replaces}");

        if (!string.IsNullOrWhiteSpace(p.PackageHomepage))
            sb.AppendLine($"Homepage: {p.PackageHomepage}");
        else if (upstreamControl != null && upstreamControl.TryGetValue("Homepage", out var upHomepage) && !string.IsNullOrWhiteSpace(upHomepage))
            sb.AppendLine($"Homepage: {upHomepage}");

        // Section: local → upstream → Debian standard "utils"
        var effectiveSection = !string.IsNullOrWhiteSpace(p.Section) ? p.Section
            : upstreamControl?.GetValueOrDefault("Section")
            ?? "utils";
        sb.AppendLine($"Section: {effectiveSection}");

        // Priority: local → upstream → Debian standard "optional"
        var effectivePriority = !string.IsNullOrWhiteSpace(p.Priority) ? p.Priority
            : upstreamControl?.GetValueOrDefault("Priority")
            ?? "optional";
        sb.AppendLine($"Priority: {effectivePriority}");

        // Breaks: local → upstream; omit when neither is set
        var effectiveBreaks = !string.IsNullOrWhiteSpace(p.Breaks) ? p.Breaks
            : upstreamControl?.GetValueOrDefault("Breaks") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(effectiveBreaks))
            sb.AppendLine($"Breaks: {effectiveBreaks}");

        // Description: first line is short desc, rest is long desc (indented with space)
        var descLines = p.PackageDescription.Split('\n');
        sb.AppendLine($"Description: {descLines[0].Trim()}");
        foreach (var line in descLines.Skip(1))
        {
            var trimmed = line.TrimEnd();
            sb.AppendLine(string.IsNullOrWhiteSpace(trimmed) ? " ." : $" {trimmed}");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Merges upstream Depends with local dependencies, deduplicating by base package name.
    /// Upstream entries come first, followed by local entries that don't appear in upstream.
    /// </summary>
    internal static List<string> MergeDepends(
        List<string> localDepends,
        Dictionary<string, string>? upstreamControl)
    {
        if (upstreamControl == null ||
            !upstreamControl.TryGetValue("Depends", out var upstreamDeps) ||
            string.IsNullOrWhiteSpace(upstreamDeps))
        {
            return [..localDepends];
        }

        var upstreamList = upstreamDeps
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Trim())
            .ToList();
        var upstreamBaseNames = new HashSet<string>(upstreamList
            .Select(d => d.Split(' ', 2)[0]), StringComparer.OrdinalIgnoreCase);
        return [..upstreamList, ..localDepends.Where(d => !upstreamBaseNames.Contains(d.Split(' ', 2)[0]))];
    }

    internal static string NormalizeTargetPath(string target)
    {
        return target.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Substitutes $(Property) variables in a string value using the current build context.
    /// </summary>
    /// <summary>
    /// Resolves $(Suite) and $(SuiteShortName) placeholders in a package version string.
    /// Pure function with no I/O — safe to call before building.
    /// </summary>
    public static string ResolvePackageVersion(
        string packageVersion,
        string suite,
        Dictionary<string, string>? suiteShortNameMap = null)
    {
        var suiteShortName = suiteShortNameMap != null && suiteShortNameMap.TryGetValue(suite, out var mapped)
            ? mapped
            : suite;
        return packageVersion
            .Replace("$(Suite)", suite)
            .Replace("$(SuiteShortName)", suiteShortName);
    }

    internal static string ResolveVariables(
        string value, AosprojProject project, string distro, string suite, string arch)
    {
        return value
            .Replace("$(Suite)", suite)
            .Replace("$(Distro)", distro)
            .Replace("$(Arch)", arch)
            .Replace("$(Architecture)", arch)
            .Replace("$(Component)", project.Component);
    }

    internal static Dictionary<string, string> ParseControlFile(string controlContent)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var valueBuilder = new StringBuilder();

        foreach (var line in controlContent.Split('\n'))
        {
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && currentKey != null)
            {
                // Continuation line
                valueBuilder.Append('\n');
                valueBuilder.Append(line.TrimStart());
            }
            else if (line.Contains(':') && !line.StartsWith(' ') && !line.StartsWith('\t'))
            {
                // New field
                if (currentKey != null)
                    result[currentKey] = valueBuilder.ToString().Trim();

                var colonIdx = line.IndexOf(':');
                currentKey = line[..colonIdx].Trim();
                valueBuilder.Clear();
                valueBuilder.Append(line[(colonIdx + 1)..].Trim());
            }
        }

        if (currentKey != null)
            result[currentKey] = valueBuilder.ToString().Trim();

        return result;
    }

    internal static string StripShebang(string script)
    {
        var lines = script.Split('\n');
        if (lines.Length > 0 && lines[0].StartsWith("#!"))
            return string.Join('\n', lines.Skip(1)).TrimStart('\n');
        return script.TrimStart('\n');
    }

    private static void EnsureParentDirectory(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        CopyDirectoryRecursive(src, dest);
    }

    private static void CopyDirectoryRecursive(string src, string dest)
    {
        foreach (var file in Directory.EnumerateFiles(src))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            var fileInfo = new FileInfo(file);
            var linkTarget = fileInfo.LinkTarget;

            if (linkTarget != null)
            {
                File.CreateSymbolicLink(destFile, linkTarget);
            }
            else
            {
                File.Copy(file, destFile, overwrite: true);
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(src))
        {
            var destDir = Path.Combine(dest, Path.GetFileName(dir));
            var dirInfo = new DirectoryInfo(dir);
            var linkTarget = dirInfo.LinkTarget;

            if (linkTarget != null)
            {
                Directory.CreateSymbolicLink(destDir, linkTarget);
            }
            else
            {
                Directory.CreateDirectory(destDir);
                CopyDirectoryRecursive(dir, destDir);
            }
        }
    }

    private static void SetFileMode(string path, UnixFileMode mode)
    {
#pragma warning disable CA1416
        try
        {
            File.SetUnixFileMode(path, mode);
        }
        catch (PlatformNotSupportedException) { }
#pragma warning restore CA1416
    }

    internal static async Task<long> ComputeDirectorySizeKbAsync(string dir)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "find",
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in new[]
        {
            ".",
            "-mindepth", "1",
            "(",
            "-path", "./DEBIAN",
            "-o",
            "-path", "./DEBIAN/*",
            ")",
            "-prune",
            "-o",
            "-printf", "%y\t%s\t%i\n"
        })
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            throw new InvalidOperationException("Cannot compute Installed-Size: 'find' was not found.", ex);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = (await errorTask).Trim();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Cannot compute Installed-Size: find exited with code {process.ExitCode}. {error}".Trim());

        long totalKb = 0;
        var seenRegularFileInodes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length != 3 || parts[0].Length != 1 || !long.TryParse(parts[1], out var sizeBytes))
                throw new InvalidOperationException($"Cannot compute Installed-Size: unexpected find output '{line}'.");

            switch (parts[0][0])
            {
                case 'f':
                    if (seenRegularFileInodes.Add(parts[2]))
                        totalKb += RoundUpToKiB(sizeBytes);
                    break;
                case 'l':
                    totalKb += RoundUpToKiB(sizeBytes);
                    break;
                default:
                    totalKb += 1;
                    break;
            }
        }

        return Math.Max(1, totalKb);
    }

    private static long RoundUpToKiB(long sizeBytes)
    {
        return Math.Max(1, (sizeBytes + 1023) / 1024);
    }

    private static async Task RunShellAsync(string command, string workingDir, Dictionary<string, string>? env = null)
    {
        await RunCommandAsync("/bin/sh", ["-c", command], workingDir, env);
    }

    private static async Task RunCommandAsync(string executable, string[] args, string workingDir, Dictionary<string, string>? env = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // Reproducible builds: fix the timestamp used by dpkg-deb so that
        // rebuilding the same source always produces byte-for-byte identical
        // output.  Without this every CI run embeds a different mtime in the
        // ar/tar archives, yielding a different SHA-256 for the same content.
        process.StartInfo.Environment["SOURCE_DATE_EPOCH"] = "0";
        if (env != null)
        {
            foreach (var (key, value) in env)
                process.StartInfo.Environment[key] = value;
        }
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Command '{executable} {string.Join(' ', args)}' failed with exit code {process.ExitCode}.");
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            File.Delete(path);
    }
}
