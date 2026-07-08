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
    public string Breaks { get; set; } = string.Empty;
    /// <summary>
    /// One or more Recommends strings, each optionally scoped to a Suite via Condition.
    /// Corresponds to the deb control <c>Recommends:</c> field.
    /// apt installs these by default but they can be removed independently.
    /// </summary>
    public List<ConditionalValue> Recommends { get; set; } = [];
    /// <summary>
    /// One or more Suggests strings, each optionally scoped to a Suite via Condition.
    /// Corresponds to the deb control <c>Suggests:</c> field.
    /// apt does not install these automatically; they are informational only.
    /// </summary>
    public List<ConditionalValue> Suggests { get; set; } = [];
    public string Component { get; set; } = "main";

    // ── Classification ──────────────────────────────────────────────────────────
    /// <summary>
    /// Debian Section (e.g. "utils", "admin", "editors"). Defaults to empty
    /// ("not set"); BuildControl falls back to "utils" when neither local nor
    /// upstream provides a value.
    /// </summary>
    public string Section { get; set; } = string.Empty;
    /// <summary>
    /// Debian Priority (e.g. "optional", "required", "important"). Defaults to
    /// empty ("not set"); BuildControl falls back to "optional" when neither
    /// local nor upstream provides a value.
    /// </summary>
    public string Priority { get; set; } = string.Empty;

    // ── Build targets ────────────────────────────────────────────────────────
    /// <summary>The target distro for this package, e.g. "ubuntu", "anduinos".</summary>
    public string TargetDistro { get; set; } = "ubuntu";
    /// <summary>Space- or comma-separated suite names, e.g. "jammy noble resolute".</summary>
    public string TargetSuites { get; set; } = string.Empty;
    /// <summary>Space- or comma-separated architectures, e.g. "amd64 arm64".</summary>
    public string TargetArchitectures { get; set; } = "amd64";

    // ── Upstream source (optional — derive from an existing .deb) ─────────────
    /// <summary>
    /// APT repository base URL of the upstream package, e.g. "http://archive.ubuntu.com/ubuntu".
    /// Supports multiple entries with Condition attributes to provide architecture-specific upstream mirrors.
    /// </summary>
    public List<ConditionalValue> UpstreamUrls { get; set; } = [];
    /// <summary>Distro identifier of the upstream repository, e.g. "ubuntu".</summary>
    public string UpstreamDistro { get; set; } = string.Empty;
    /// <summary>Package name to derive from, e.g. "base-files".</summary>
    public string UpstreamPackage { get; set; } = string.Empty;
    /// <summary>Upstream suite to pull from. Supports $(Suite) variable. e.g. "resolute", "$(Suite)".</summary>
    public string UpstreamSuite { get; set; } = string.Empty;
    /// <summary>
    /// Optional mapping from output suite to upstream suite.
    /// Format: "output1=upstream1 output2=upstream2"
    /// When set, resolved $(Suite) values are looked up here before downloading from upstream.
    /// </summary>
    public string UpstreamSuiteMapping { get; set; } = string.Empty;
    /// <summary>Upstream APT component, e.g. "main".</summary>
    public string UpstreamComponent { get; set; } = "main";
    /// <summary>Architecture of the upstream package, e.g. "all", "amd64".</summary>
    public string UpstreamArch { get; set; } = "all";
    /// <summary>
    /// Path to a GPG keyring file used to verify the upstream APT repository
    /// via the [signed-by=…] apt source option. The path is relative to the
    /// project root. When set, the keyring is copied into the isolated apt
    /// temp directory during build and automatically cleaned up afterwards.
    /// </summary>
    public string UpstreamSignedBy { get; set; } = string.Empty;
    /// <summary>
    /// When true, upstream maintainer scripts (postinst, prerm, postrm) are NOT
    /// prepended to the package's own scripts. Use this when deriving from an
    /// upstream .deb solely for its data payload and you want full control over
    /// maintainer scripts via PostInstallScript / PreRemoveScript.
    /// </summary>
    public bool SuppressUpstreamScripts { get; set; }
    /// <summary>
    /// Space/comma-separated list of upstream package names to strip from the
    /// inherited Depends before merging local dependencies.
    /// e.g. "ubuntu-pro-client ubuntu-advantage-desktop-daemon".
    /// </summary>
    public string SuppressUpstreamDependencies { get; set; } = string.Empty;
    /// <summary>
    /// When true, automatically converts exact version dependencies (=) inherited from upstream
    /// to greater-than-or-equal (>=) dependencies. This solves cross-source package exact version
    /// pinning issues where an inherited exact version blocks system upgrades when upstream patches are released.
    /// Defaults to true.
    /// </summary>
    public bool AutoConvertUpstreamExactVersions { get; set; } = true;

    // ── Dependency check ─────────────────────────────────────────────────────
    /// <summary>
    /// One or more APT repositories to validate declared dependencies against.
    /// Each entry defines a Url (apt server base URL) and optional SuiteMap
    /// (target suite → check suite mapping) and Condition.
    /// A dependency passes validation if it is found in ANY configured source (union semantics).
    /// Leave empty to skip dependency validation entirely.
    /// </summary>
    public List<DependencyCheckSourceItem> DependencyCheckSources { get; set; } = [];

    /// <summary>
    /// Maps target suite names to the short suite names used in <c>$(SuiteShortName)</c>
    /// version placeholder expansion. Same format as upstream suite mapping:
    /// space/comma-separated "target=short" pairs.
    /// e.g. "noble-addon=noble questing-addon=questing resolute-addon=resolute"
    /// </summary>
    public string SuiteShortNameMap { get; set; } = string.Empty;

    // ── Items ────────────────────────────────────────────────────────────────
    public List<PrebuildCommandItem> PrebuildCommands { get; set; } = [];
    public List<IncludeFileItem> IncludeFiles { get; set; } = [];
    public List<IncludeFolderItem> IncludeFolders { get; set; } = [];
    public List<IncludeScriptItem> IncludeScripts { get; set; } = [];
    public List<ConfFileItem> ConfFiles { get; set; } = [];
    public List<PostInstallScriptItem> PostInstallScripts { get; set; } = [];
    public List<PreRemoveScriptItem> PreRemoveScripts { get; set; } = [];
    public List<PreInstallScriptItem> PreInstallScripts { get; set; } = [];
    public List<PostRemoveScriptItem> PostRemoveScripts { get; set; } = [];
    public List<SystemdUnitItem> SystemdUnits { get; set; } = [];
    public List<DpkgTriggerItem> DpkgTriggers { get; set; } = [];

    // ── Computed helpers ─────────────────────────────────────────────────────
    public string[] SuiteList => Split(TargetSuites);
    public string[] ArchList => Split(TargetArchitectures);
    /// <summary>True when this project derives from an upstream .deb (base-files pattern).</summary>
    public bool HasUpstreamSource => !string.IsNullOrWhiteSpace(UpstreamPackage);

    public Dictionary<string, string> GetUpstreamSuiteMap() => ParseSuiteMap(UpstreamSuiteMapping);

    /// <summary>
    /// Parses <see cref="SuiteShortNameMap"/> into a dictionary: target suite → short name.
    /// </summary>
    public Dictionary<string, string> GetSuiteShortNameMap() => ParseSuiteMap(SuiteShortNameMap);

    private static Dictionary<string, string> ParseSuiteMap(string raw)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw))
            return map;

        var normalized = System.Text.RegularExpressions.Regex.Replace(raw, @"\s*=\s*", "=");
        foreach (var pair in Split(normalized))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0 && eqIdx < pair.Length)
                map[pair[..eqIdx]] = pair[(eqIdx + 1)..];
        }
        return map;
    }

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

    /// <summary>
    /// Optional Unix permission mode (octal string like "755", "644").
    /// When set, the copied file will have these permissions applied.
    /// When null, source permissions are preserved (IncludeFile) or default
    /// to 0755 (IncludeScript).
    /// </summary>
    public UnixFileMode? Mode { get; set; }
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

/// <summary>An APT repository source for dependency validation during lint.</summary>
public class DependencyCheckSourceItem
{
    public string Url { get; set; } = string.Empty;
    public string SuiteMap { get; set; } = string.Empty;
    public string? Condition { get; set; }

    /// <summary>Parses SuiteMap into dictionary: target suite → check suite.</summary>
    public Dictionary<string, string> GetSuiteMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(SuiteMap)) return map;
        var normalized = System.Text.RegularExpressions.Regex.Replace(SuiteMap, @"\s*=\s*", "=");
        foreach (var pair in normalized.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIdx = pair.IndexOf('=');
            if (eqIdx > 0 && eqIdx < pair.Length)
                map[pair[..eqIdx]] = pair[(eqIdx + 1)..];
        }
        return map;
    }
}

/// <summary>Shell script to invoke after the package is installed (becomes DEBIAN/postinst).</summary>
public class PostInstallScriptItem : BaseItem { }

/// <summary>Shell script to invoke before the package is removed (becomes DEBIAN/prerm).</summary>
public class PreRemoveScriptItem : BaseItem { }

/// <summary>Shell script to invoke before the package is installed (becomes DEBIAN/preinst).</summary>
public class PreInstallScriptItem : BaseItem { }

/// <summary>Shell script to invoke after the package is removed (becomes DEBIAN/postrm).</summary>
public class PostRemoveScriptItem : BaseItem { }

/// <summary>A systemd unit file to ship and optionally auto-enable on install.</summary>
public class SystemdUnitItem : BaseItem
{
    public bool AutoEnable { get; set; } = true;
    /// <summary>
    /// When true, postinst uses <c>systemctl preset</c> instead of <c>systemctl enable</c>.
    /// Use this when the package also ships a systemd preset file
    /// (e.g. <c>/usr/lib/systemd/system-preset/90-*.preset</c>) that defines the enable policy.
    /// Defaults to false — most packages should use the standard <c>systemctl enable</c> path.
    /// </summary>
    public bool UsePreset { get; set; }
}

/// <summary>
/// Declares a dpkg trigger interest or activation directive.
/// Each item becomes a line in the generated DEBIAN/triggers file.
/// </summary>
public class DpkgTriggerItem
{
    /// <summary>
    /// Trigger directive type: "interest", "interest-noawait",
    /// "activate", or "activate-noawait".
    /// Defaults to "interest-noawait".
    /// </summary>
    public string Type { get; set; } = "interest-noawait";

    /// <summary>
    /// The trigger name — typically a filesystem path such as "/etc/dconf/db".
    /// </summary>
    public string Name { get; set; } = string.Empty;
}
