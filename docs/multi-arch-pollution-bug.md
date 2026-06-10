# Multi-arch pollution: apt fetches foreign arch Packages for UpstreamArch=all

## Symptoms

CI builds fail with exit code 100 when the `.aosproj` declares
`UpstreamArch=all` and the CI runner host has a foreign architecture
registered (e.g. arm64 registered on an amd64 host):

```
E: Failed to fetch https://mirror.aiursoft.com/ubuntu/dists/noble/main/binary-arm64/Packages  404  Not Found
Unhandled exception: System.InvalidOperationException:
  Command 'apt-get update ...' failed with exit code 100.
```

Affected packages include any package deriving from an `all` upstream:
- `firmware-sof-anduinos` (UpstreamArch=all, derives from firmware-sof-signed)
- Any other package with `UpstreamArch=all` built on a multi-arch CI host

## Root cause

`DebBuilder.DownloadUpstreamDebAsync()` adds an `[arch=...]` qualifier to
the temporary apt source to prevent multi-arch pollution (added in
commit 13a7cc3). However it **skips** the qualifier when the upstream
package is architecture `all`:

```csharp
// Line 516-517 of DebBuilder.cs
if (!string.Equals(resolvedUpstreamArch, "all", StringComparison.OrdinalIgnoreCase)
    && !string.IsNullOrEmpty(resolvedUpstreamArch))
```

The reasoning was: "binary-all packages don't need an arch qualifier —
`binary-all` is always fetched and never 404s."

**This reasoning is wrong.** The problem isn't with `binary-all` — it's with
the FOREIGN architectures. When the build host has arm64 registered (via
`dpkg --add-architecture arm64`, which is necessary for cross-compiling
ARM packages like `anduinos-deskmon`), apt-get update fetches Packages
for **every** registered architecture. Even for `all` upstreams, apt
will try to fetch `binary-arm64/Packages`, and that 404s on mirrors
that only carry amd64 (archive.ubuntu.com, mirror.aiursoft.com).

The `[arch=...]` qualifier is NOT about specifying which architecture
to download — it's about **limiting which architecture indexes to fetch**.
Without it, apt fetches indexes for all registered architectures.

## Reproduction

1. On an amd64 host: `sudo dpkg --add-architecture arm64 && sudo apt update`
2. Build `firmware-sof-anduinos` (or any package with `UpstreamArch=all`)
3. `apkg publish` fails with binary-arm64 404

## Proposed fix

When `resolvedUpstreamArch` is `all`, fall back to the build **target**
architecture as the qualifier:

```diff
- if (!string.Equals(resolvedUpstreamArch, "all", StringComparison.OrdinalIgnoreCase)
-     && !string.IsNullOrEmpty(resolvedUpstreamArch))
+ // When upstream is "all", use the build target arch to prevent
+ // foreign arch index fetches from 404'ing on single-arch mirrors.
+ var effectiveArch = string.Equals(resolvedUpstreamArch, "all", StringComparison.OrdinalIgnoreCase)
+     ? <build-target-arch>
+     : resolvedUpstreamArch;
+
+ if (!string.IsNullOrEmpty(effectiveArch))
```

Note: `<build-target-arch>` is the `arch` parameter from `BuildAsync()`
(e.g. `amd64`, `arm64`). It is currently not passed down to
`DownloadUpstreamDebAsync()` — it needs to be added as a parameter.

Alternatively, use `RuntimeInformation.ProcessArchitecture` to derive a
host-appropriate default when the target arch is not available.

## Affected packages in AnduinOS-Packages

Any package with `UpstreamArch=all` built on the multi-arch CI runner:
- `firmware-sof-anduinos`
- `base-files`
- Potentially others with `UpstreamArch=all`

## Similar issue in prebuild scripts

The same multi-arch pollution can affect `.aosproj` prebuild scripts
that use raw `apt-get` without `[arch=...]`. Example:
`anduinos-gnome-shell-locale/download.sh`. These must also add
`[arch=amd64]` to their apt sources (fixed separately).
