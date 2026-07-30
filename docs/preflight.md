# Build preflight

Apkg can avoid rebuilding an unchanged project by resolving the exact package
coordinates before the build and asking the destination server whether every
coordinate is already present.

## Safety invariant

Preflight is an optimization, not a replacement for upload validation.

The build is skipped only when the authenticated server returns `allPresent:
true` for the complete requested target matrix. Any missing target causes the
normal full-matrix build. The later upload still performs hash, duplicate,
permission, and downgrade checks, which makes concurrent deployments safe.

The server uses the same repository matching and authorization service for
preflight and upload. A package is present only when an enabled row exists for
every matching repository the caller is authorized to upload to, with the exact
tuple:

```text
(repository, package name, version, architecture)
```

The component selects matching repositories but is intentionally not part of
the duplicate slot, matching the existing upload semantics.

## Version resolution

```bash
apkg guess version --path ./my-package
apkg guess-version --path ./my-package --json
```

Both spellings are equivalent. The command resolves `$(Suite)` and
`$(SuiteShortName)` locally. When `PackageVersion` contains
`$(UpstreamVersion)`, it reads and verifies upstream APT index metadata and
selects the same highest-version candidate as the package builder. It does not
download the upstream `.deb` or execute prebuild commands.

Unresolved version variables are errors. Apkg never sends an uncertain version
to the preflight API.

## Deployment command

`apkg deploy` composes the existing publish and push operations:

```bash
apkg deploy \
  --path ./my-package \
  --source https://apkg.example.com \
  --api-key "$APKG_API_KEY" \
  --skip-existing
```

Without `--skip-existing`, `deploy` is equivalent to `publish` followed by
`push`. With it, Apkg resolves the complete plan and calls the preflight API
before building. `--skip-existing` also enables duplicate skipping during the
final upload to handle a race where another job publishes between the check and
the upload.

Compatibility behavior:

- `401` or `403`: fail immediately; building with an invalid credential would
  only waste resources.
- `404`, `405`, or `5xx`: log a warning and use the legacy full build and push,
  allowing clients to be upgraded before servers.
- Network failure: log a warning and use the legacy full build and push.
- `NoRepository`: perform the legacy build and let the existing upload warning
  behavior apply.
- `Forbidden` in a successful response: fail before building.

## API

`POST /api/packages/preflight` uses the same bearer API key authentication as
package upload.

Request:

```json
{
  "name": "my-package",
  "distro": "anduinos",
  "component": "main",
  "targets": [
    {
      "suite": "resolute-addon",
      "architecture": "amd64",
      "version": "2.0.1-4+resolute"
    }
  ]
}
```

Response:

```json
{
  "allPresent": false,
  "targets": [
    {
      "suite": "resolute-addon",
      "architecture": "amd64",
      "version": "2.0.1-4+resolute",
      "status": "Missing",
      "message": "At least one matching repository is missing this version.",
      "repositories": [
        {
          "id": 1,
          "name": "anduinos (anduinos resolute-addon amd64)",
          "present": false
        }
      ]
    }
  ]
}
```

Target status is one of `Present`, `Missing`, `NoRepository`, or `Forbidden`.
Requests are limited to 256 targets.

## GitLab CI migration

Existing jobs can keep their current matrix and rules. Only the shared publish
template needs to change:

```yaml
.publish:
  stage: publish
  script:
    - cd "$PACKAGE_DIR"
    - |
      if [ "$CI_COMMIT_BRANCH" = "prod" ]; then
        APKG_SOURCE="https://apkg.aiursoft.com"
        APKG_KEY="$APKG_PROD_API_KEY"
      else
        APKG_SOURCE="https://apkg-dev.aiursoft.com"
        APKG_KEY="$APKG_DEV_API_KEY"
      fi
      apkg deploy \
        --source "$APKG_SOURCE" \
        --api-key "$APKG_KEY" \
        --skip-existing
```

This deliberately keeps all package jobs. They become inexpensive metadata
checks when unchanged, while upstream-derived projects can still notice a new
upstream version even when their Git directory did not change. Development and
production query their own servers, so their intentional version divergence is
preserved.
