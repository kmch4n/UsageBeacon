# Release Procedure

Releases are built and published by `.github/workflows/release.yml` when a `v*` tag is pushed. The
workflow uses only the automatic `GITHUB_TOKEN`; no additional secrets are configured.

## Before tagging

1. Make sure `main` is green in CI and the working tree is clean.
2. Update `<Version>` in `UsageBeacon/UsageBeacon.csproj`.
3. In `docs/CHANGELOG.md`, rename `## Unreleased` to `## X.Y.Z - YYYY-MM-DD` and add a fresh
   `## Unreleased` heading above it.
4. Run the local checks with UsageBeacon not running, because a live process locks the output paths:

    ```powershell
    dotnet test UsageBeacon.sln -c Debug
    dotnet build UsageBeacon.sln -c Debug
    dotnet build UsageBeacon.sln -c Release
    ```

   `ReleaseMetadataTests` fails if the project version and the newest changelog heading disagree.
5. Commit the version and changelog changes.

## Tagging

```powershell
git tag v1.1.0
git push fork v1.1.0
```

The tag name must be the project version with a leading `v`. The workflow verifies this before it
builds anything and fails the run on a mismatch, so a published asset can never claim a version it
was not built from.

## What the workflow does

1. Verifies the tag against `<Version>`.
2. Runs the test suite.
3. Publishes the self-contained single-file `win-x64` executable.
4. Writes `UsageBeacon.exe.sha256` next to it.
5. Creates the GitHub release with both files attached and generated notes.

## After the run

- Confirm both assets are attached and that the published checksum matches the executable.
- Update `.memory/STATUS.md` with the release date and the verification result.

Users can verify a download with:

```powershell
Get-FileHash .\UsageBeacon.exe -Algorithm SHA256
```
