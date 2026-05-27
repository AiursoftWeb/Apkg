using System.Diagnostics;
using System.Text;
using Aiursoft.Apkg.Sdk.Models;
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

    /// <summary>
    /// Builds a .deb for the given target and writes it to <paramref name="outputDir"/>.
    /// Returns the absolute path to the produced .deb file.
    /// </summary>
    public async Task<string> BuildAsync(
        string projectDir,
        AosprojProject project,
        string distro,
        string suite,
        string arch,
        string outputDir)
    {
        var ctx = ConditionEvaluator.BuildContext(distro, suite, arch);
        bool Include(string? cond) => _evaluator.Evaluate(cond, ctx);

        // ── Prebuild commands ────────────────────────────────────────────────
        foreach (var cmd in project.PrebuildCommands.Where(c => Include(c.Condition)))
        {
            _logger.LogInformation("Running prebuild command: {Cmd}", cmd.Run);
            await RunShellAsync(cmd.Run, projectDir);
        }

        // ── Staging directory: obj/<suite>_<arch> ────────────────────────────
        var stagingRoot = Path.Combine(projectDir, "obj", $"{suite}_{arch}");
        if (Directory.Exists(stagingRoot))
            Directory.Delete(stagingRoot, recursive: true);
        Directory.CreateDirectory(stagingRoot);

        var debianDir = Path.Combine(stagingRoot, "DEBIAN");
        Directory.CreateDirectory(debianDir);

        // ── DEBIAN/control ───────────────────────────────────────────────────
        var depends = project.Dependencies
            .Where(d => Include(d.Condition))
            .Select(d => d.Value.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        var control = BuildControl(project, arch, depends);
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
            _logger.LogDebug("  + {Target}", item.Target);
        }

        // ── Copy IncludeScript items (executable, 0755) ───────────────────────
        foreach (var item in project.IncludeScripts.Where(f => Include(f.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            var dest = Path.Combine(stagingRoot, NormalizeTargetPath(item.Target));
            EnsureParentDirectory(dest);
            File.Copy(src, dest, overwrite: true);
            MakeExecutable(dest);
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
            _logger.LogDebug("  + /lib/systemd/system/{Unit}", unitName);
        }

        // ── PostInstallScript → DEBIAN/postinst ───────────────────────────────
        var postinstLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPostinst = false;

        foreach (var item in project.PostInstallScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            postinstLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPostinst = true;
        }

        // Systemd postinst: enable+start on fresh install, try-restart on upgrade
        if (autoEnableUnits.Count > 0)
        {
            postinstLines.AppendLine("case \"$1\" in");
            postinstLines.AppendLine("    configure)");
            postinstLines.AppendLine("        systemctl daemon-reload");
            postinstLines.AppendLine("        if [ -z \"$2\" ]; then");
            foreach (var unit in autoEnableUnits)
            {
                var un = Path.GetFileName(unit.Source);
                postinstLines.AppendLine($"            systemctl enable {un}");
                postinstLines.AppendLine($"            systemctl start {un} || true");
            }
            postinstLines.AppendLine("        else");
            foreach (var unit in autoEnableUnits)
                postinstLines.AppendLine($"            systemctl try-restart {Path.GetFileName(unit.Source)} || true");
            postinstLines.AppendLine("        fi");
            postinstLines.AppendLine("    ;;");
            postinstLines.AppendLine("esac");
            hasPostinst = true;
        }

        if (hasPostinst)
        {
            var postinstPath = Path.Combine(debianDir, "postinst");
            await File.WriteAllTextAsync(postinstPath, postinstLines.ToString());
            MakeExecutable(postinstPath);
        }

        // ── PreRemoveScript → DEBIAN/prerm ────────────────────────────────────
        var prermLines = new StringBuilder("#!/bin/sh\nset -e\n");
        bool hasPrerm = false;

        foreach (var item in project.PreRemoveScripts.Where(s => Include(s.Condition)))
        {
            var src = Path.GetFullPath(Path.Combine(projectDir, item.Source));
            prermLines.AppendLine(await File.ReadAllTextAsync(src));
            hasPrerm = true;
        }

        // Systemd prerm: stop only on remove, skip on upgrade (postinst will try-restart)
        if (autoEnableUnits.Count > 0)
        {
            prermLines.AppendLine("case \"$1\" in");
            prermLines.AppendLine("    remove|deconfigure)");
            foreach (var unit in autoEnableUnits)
                prermLines.AppendLine($"        systemctl stop {Path.GetFileName(unit.Source)} || true");
            prermLines.AppendLine("    ;;");
            prermLines.AppendLine("esac");
            hasPrerm = true;
        }

        if (hasPrerm)
        {
            var prermPath = Path.Combine(debianDir, "prerm");
            await File.WriteAllTextAsync(prermPath, prermLines.ToString());
            MakeExecutable(prermPath);
        }

        // ── DEBIAN/postrm (disable + daemon-reload on remove/purge) ───────────
        if (autoEnableUnits.Count > 0)
        {
            var postrmLines = new StringBuilder("#!/bin/sh\nset -e\n");
            postrmLines.AppendLine("case \"$1\" in");
            postrmLines.AppendLine("    remove|purge)");
            foreach (var unit in autoEnableUnits)
                postrmLines.AppendLine($"        systemctl disable {Path.GetFileName(unit.Source)} || true");
            postrmLines.AppendLine("        systemctl daemon-reload || true");
            postrmLines.AppendLine("    ;;");
            postrmLines.AppendLine("esac");
            var postrmPath = Path.Combine(debianDir, "postrm");
            await File.WriteAllTextAsync(postrmPath, postrmLines.ToString());
            MakeExecutable(postrmPath);
        }

        // ── Compute installed-size (kibibytes) ────────────────────────────────
        var installedSizeKb = ComputeDirectorySizeKb(stagingRoot);
        // Patch control with Installed-Size
        var controlText = await File.ReadAllTextAsync(Path.Combine(debianDir, "control"));
        controlText = controlText.Replace("__INSTALLED_SIZE__", installedSizeKb.ToString());
        await File.WriteAllTextAsync(Path.Combine(debianDir, "control"), controlText);

        // ── dpkg-deb --build ──────────────────────────────────────────────────
        Directory.CreateDirectory(outputDir);
        var debFileName = $"{project.PackageName}_{project.PackageVersion}_{suite}_{arch}.deb";
        var debOutputPath = Path.Combine(outputDir, debFileName);

        _logger.LogInformation("Building {DebFile}...", debFileName);
        await RunCommandAsync("dpkg-deb", ["--build", "--root-owner-group", stagingRoot, debOutputPath], projectDir);

        _logger.LogInformation("  ✓ {DebFile}", debOutputPath);
        return debOutputPath;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildControl(AosprojProject p, string arch, List<string> depends)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Package: {p.PackageName}");
        sb.AppendLine($"Version: {p.PackageVersion}");
        sb.AppendLine($"Architecture: {arch}");
        sb.AppendLine($"Maintainer: {(string.IsNullOrWhiteSpace(p.Maintainer) ? p.PackageAuthors : p.Maintainer)}");
        sb.AppendLine($"Installed-Size: __INSTALLED_SIZE__");
        if (depends.Count > 0)
            sb.AppendLine($"Depends: {string.Join(", ", depends)}");
        if (!string.IsNullOrWhiteSpace(p.Provides))
            sb.AppendLine($"Provides: {p.Provides}");
        if (!string.IsNullOrWhiteSpace(p.Conflicts))
            sb.AppendLine($"Conflicts: {p.Conflicts}");
        if (!string.IsNullOrWhiteSpace(p.Replaces))
            sb.AppendLine($"Replaces: {p.Replaces}");
        if (!string.IsNullOrWhiteSpace(p.PackageHomepage))
            sb.AppendLine($"Homepage: {p.PackageHomepage}");

        // Description: first line is short desc, rest is long desc (indented with space)
        var descLines = p.PackageDescription.Split('\n', StringSplitOptions.None);
        sb.AppendLine($"Description: {descLines[0].Trim()}");
        foreach (var line in descLines.Skip(1))
        {
            var trimmed = line.TrimEnd();
            sb.AppendLine(string.IsNullOrWhiteSpace(trimmed) ? " ." : $" {trimmed}");
        }

        return sb.ToString();
    }

    private static string NormalizeTargetPath(string target)
    {
        // Strip leading slash so we can Path.Combine with the staging root
        return target.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
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
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file);
            var destFile = Path.Combine(dest, relative);
            EnsureParentDirectory(destFile);
            File.Copy(file, destFile, overwrite: true);
        }
    }

    private static void MakeExecutable(string path)
    {
        // Only meaningful on Unix; silently ignore on Windows
        try
        {
            var info = new FileInfo(path);
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (PlatformNotSupportedException) { }
    }

    private static long ComputeDirectorySizeKb(string dir)
    {
        long totalBytes = Directory
            .EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "DEBIAN" + Path.DirectorySeparatorChar))
            .Sum(f => new FileInfo(f).Length);
        return Math.Max(1, totalBytes / 1024);
    }

    private static async Task RunShellAsync(string command, string workingDir)
    {
        await RunCommandAsync("/bin/sh", ["-c", command], workingDir);
    }

    private static async Task RunCommandAsync(string executable, string[] args, string workingDir)
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
        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Command '{executable} {string.Join(' ', args)}' failed with exit code {process.ExitCode}.");
    }
}
