# firefox-anduinos: "Package not found" on Mozilla APT repo

## Status

GPG fix (9fbb3a9) works — no more keyring errors.  New issue: package
search fails after GPG passes.

## Symptom

```
[INFO] Using GPG keyring assets/mozilla-keyring.asc for upstream verification.
[INFO] Downloading firefox from https://packages.mozilla.org/apt (mozilla)...
[ERROR] Package 'firefox' not found in https://packages.mozilla.org/apt
        suite mozilla component main.
```

## Root cause

Mozilla's APT repo **only serves uncompressed** `Packages` files.
No `.gz`, no `.xz`.

```bash
$ curl -s "https://packages.mozilla.org/apt/dists/mozilla/Release" | grep Architectures
Architectures: all amd64 arm64 i386

$ curl -s "https://packages.mozilla.org/apt/dists/mozilla/main/binary-amd64/Packages.gz"
Requested entity was not found.

$ curl -s "https://packages.mozilla.org/apt/dists/mozilla/main/binary-amd64/Packages"
<valid Packages data, 120KB>
```

Ubuntu and Debian mirrors always serve compressed variants.  Mozilla does not.

`AptPackageSource.FetchPackagesAsync()` checks `supportedFiles` (from
InRelease) for each format: xz → gz → raw.  Mozilla's InRelease lists
only `main/binary-amd64/Packages` (raw), so the first two checks fail
and the third should succeed.  But it doesn't — the reason needs
further investigation.

## Reproduction

```bash
curl -s "https://packages.mozilla.org/apt/dists/mozilla/InRelease" | grep "Packages"
# → only uncompressed: main/binary-all/Packages, main/binary-amd64/Packages, etc.

# Expected: AptPackageSource falls back to raw and finds firefox
# Actual: "Package 'firefox' not found"
```

## Affected packages

- `firefox-anduinos` (only package using Mozilla APT repo)

## Acceptance criteria

- [ ] `firefox-anduinos` builds with `apkg publish` against live Mozilla APT repo
