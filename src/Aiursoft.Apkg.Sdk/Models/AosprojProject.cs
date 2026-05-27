namespace Aiursoft.Apkg.Sdk.Models;

/// <summary>
/// Represents an .aosproj project file — the source-of-truth for building .deb packages.
/// </summary>
public class AosprojProject
{
    // ── Core identity ────────────────────────────────────────────────────────
    public string PackageName { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = "1.0.0";
    public string PackageDescription { get; set; } = string.Empty;
    public string PackageAuthors { get; set; } = string.Empty;
    public string Maintainer { get; set; } = string.Empty;
    public string PackageHomepage { get; set; } = string.Empty;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string LicenseType { get; set; } = "MIT";
    public string LicenseFile { get; set; } = string.Empty;
    public string PackageTags { get; set; } = string.Empty;

    // ── APT metadata ─────────────────────────────────────────────────────────
    /// <summary>
    /// One or more Depends strings, each optionally scoped to a Suite via Condition.
    /// e.g. Condition="'$(Suite)' == 'jammy'"
    /// </summary>
    public List<ConditionalValue> Dependencies { get; set; } = [];
    public string Provides { get; set; } = string.Empty;
    public string Conflicts { get; set; } = string.Empty;
    public string Replaces { get; set; } = string.Empty;
    public string Component { get; set; } = "main";

    // ── Build targets ────────────────────────────────────────────────────────
    /// <summary>The target distro for this package, e.g. "ubuntu", "anduinos".</summary>
    public string TargetDistro { get; set; } = "ubuntu";
    /// <summary>Space- or comma-separated suite names, e.g. "jammy noble resolute".</summary>
    public string TargetSuites { get; set; } = string.Empty;
    /// <summary>Space- or comma-separated architectures, e.g. "amd64 arm64".</summary>
    public string TargetArchitectures { get; set; } = "amd64";

    // ── Items ────────────────────────────────────────────────────────────────
    public List<PrebuildCommandItem> PrebuildCommands { get; set; } = [];
    public List<IncludeFileItem> IncludeFiles { get; set; } = [];
    public List<IncludeFolderItem> IncludeFolders { get; set; } = [];
    public List<IncludeScriptItem> IncludeScripts { get; set; } = [];
    public List<ConfFileItem> ConfFiles { get; set; } = [];
    public List<PostInstallScriptItem> PostInstallScripts { get; set; } = [];
    public List<PreRemoveScriptItem> PreRemoveScripts { get; set; } = [];
    public List<SystemdUnitItem> SystemdUnits { get; set; } = [];

    // ── Computed helpers ─────────────────────────────────────────────────────
    public string[] SuiteList => Split(TargetSuites);
    public string[] ArchList => Split(TargetArchitectures);

    private static string[] Split(string value) =>
        value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>A string value that may carry an MSBuild-style Condition attribute.</summary>
public class ConditionalValue
{
    public string? Condition { get; set; }
    public string Value { get; set; } = string.Empty;
}

public abstract class BaseItem
{
    public string Source { get; set; } = string.Empty;
    public string? Condition { get; set; }
}

/// <summary>Copies a single file to a fixed target path inside the package.</summary>
public class IncludeFileItem : BaseItem
{
    public string Target { get; set; } = string.Empty;
}

/// <summary>Recursively copies a directory to a target path inside the package.</summary>
public class IncludeFolderItem : BaseItem
{
    public string Target { get; set; } = string.Empty;
}

/// <summary>
/// Copies a file, sets its permissions to 0755 (executable), and installs it.
/// Use for scripts and binaries that should be runnable after installation.
/// </summary>
public class IncludeScriptItem : BaseItem
{
    public string Target { get; set; } = string.Empty;
}

/// <summary>
/// Copies a file and marks it as a dpkg conffile (preserved on upgrade when user-modified).
/// </summary>
public class ConfFileItem : BaseItem
{
    public string Target { get; set; } = string.Empty;
}

/// <summary>A shell command to run before the deb is assembled (e.g. compile a binary).</summary>
public class PrebuildCommandItem
{
    public string Run { get; set; } = string.Empty;
    public string? Condition { get; set; }
}

/// <summary>Shell script to invoke after the package is installed (becomes DEBIAN/postinst).</summary>
public class PostInstallScriptItem : BaseItem { }

/// <summary>Shell script to invoke before the package is removed (becomes DEBIAN/prerm).</summary>
public class PreRemoveScriptItem : BaseItem { }

/// <summary>A systemd unit file to ship and optionally auto-enable on install.</summary>
public class SystemdUnitItem : BaseItem
{
    public bool AutoEnable { get; set; } = true;
}
