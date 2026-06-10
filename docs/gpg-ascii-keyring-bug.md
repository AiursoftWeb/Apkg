# GPG verification fails with ASCII-armored keyrings

## Symptom

CI: `firefox-anduinos` build fails.  `firmware-sof-anduinos` succeeds.

```
gpgv: invalid packet (ctb=2d)
gpgv: Can't check signature: No public key
```

## Root cause

`AptGpgVerifier.VerifyFileAsync()` passes the keyring file directly to
`gpgv --keyring <path>`.  `gpgv` does **not** support ASCII-armored
keyrings — it requires binary (`.gpg`) format.

`firmware-sof-anduinos` works because it has no `UpstreamSignedBy` →
`allowInsecure=true` → skips verification entirely.

## Reproduction

```bash
# Fails: ASCII keyring
gpgv --keyring firefox-anduinos/assets/mozilla-keyring.asc /tmp/inrelease
# → invalid packet (ctb=2d), NO_PUBKEY

# Works: binary keyring
gpg --dearmor < firefox-anduinos/assets/mozilla-keyring.asc > /tmp/keyring.gpg
gpgv --keyring /tmp/keyring.gpg /tmp/inrelease
# → GOODSIG
```

## Fix

`src/Aiursoft.AptClient/AptGpgVerifier.cs` → `VerifyFileAsync()`

Before:

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = "gpgv",
    Arguments = $"--status-fd 1 --keyring \"{keyringPath}\" \"{signedFilePath}\"",
    ...
};
```

After:

```csharp
// gpgv only supports binary keyrings. Convert ASCII-armored keys first.
string actualKeyring = keyringPath;
if (keyringPath.EndsWith(".asc", StringComparison.OrdinalIgnoreCase) ||
    (File.Exists(keyringPath) && File.ReadAllText(keyringPath).StartsWith("-----BEGIN PGP")))
{
    var tempKeyring = Path.GetTempFileName();
    var psi = new ProcessStartInfo
    {
        FileName = "gpg",
        Arguments = $"--dearmor --output \"{tempKeyring}\" \"{keyringPath}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    using var p = Process.Start(psi)!;
    await p.WaitForExitAsync();
    if (p.ExitCode != 0)
        throw new InvalidOperationException($"Failed to dearmor keyring: {keyringPath}");
    actualKeyring = tempKeyring;
}

var startInfo = new ProcessStartInfo
{
    FileName = "gpgv",
    Arguments = $"--status-fd 1 --keyring \"{actualKeyring}\" \"{signedFilePath}\"",
    ...
};
```

## Acceptance criteria

- [ ] `firefox-anduinos` builds with `apkg publish` against live Mozilla APT repo
- [ ] Existing `UpstreamSignedBy` packages (any with binary `.gpg` keyring) still work
- [ ] `gpgv --keyring <path>` never receives an ASCII-armored file

## Test requirements

- [ ] Unit test: `VerifyFileAsync` with `.asc` (ASCII) keyring → converts and succeeds
- [ ] Unit test: `VerifyFileAsync` with `.gpg` (binary) keyring → works unchanged
- [ ] Unit test: `VerifyFileAsync` with missing keyring → original error behavior preserved
