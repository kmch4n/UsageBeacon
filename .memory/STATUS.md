# Current Repository Status

Last verified: 2026-07-19

## Git and naming state

- The active development branch is `main`.
- The `fork` remote points to `https://github.com/kmch4n/UsageBeacon.git`.
- The `origin` remote points to `https://github.com/satonico/Token-Checker-win`.
- Product, solution, projects, namespaces, and executable have been renamed to UsageBeacon.
- The GitHub repository has been renamed to `kmch4n/UsageBeacon`, matching the clone and release URLs in the README.

Remote facts are drift-prone and must be verified with `git remote -v` before relying on them.

## Shared agent configuration

- `.codex/AGENTS.md` is the canonical repository agent guidance.
- `.claude/CLAUDE.md` imports that guidance so both agent environments use the same rules.
- `.claude/settings.local.json`, `.codex/config.local.toml`, and `.memory/local/` are explicitly local-only and ignored.

## Last validation

The runtime localization changes were validated on 2026-07-18:

- `dotnet test UsageBeacon.sln -c Debug`: 27 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug`: 0 warnings, 0 errors.
- `dotnet build UsageBeacon.sln -c Release`: 0 warnings, 0 errors when built to an alternate output path.
- A self-contained win-x64 single-file publish completed and produced one executable containing the localization resources.
- Automated tests verified English and Japanese resource-key and format-placeholder parity, runtime language changes, localized domain errors, unsupported-language fallback, and legacy settings compatibility.
- Manual English and Japanese popup layout verification remains pending because a previous UsageBeacon build was running during validation.

When a running UsageBeacon process locks an output path or the single-instance mutex, use an alternate output directory for automated validation. Stop the running application only with user awareness before interactive validation.

## OAuth credential persistence validation

The restart authentication fix was validated on 2026-07-19:

- `dotnet test UsageBeacon.sln -c Debug --no-restore`: 37 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug --no-restore`: 0 warnings, 0 errors.
- `dotnet build UsageBeacon.sln -c Release --no-restore`: 0 warnings, 0 errors.
- A self-contained win-x64 single-file publish completed in `publish/latest`.
- Automated tests cover rotated and unrotated refresh tokens, restart-equivalent provider recreation, unsupported credential sources, concurrent fetches, pending credentials after persistence failure, full OAuth-state conflicts, malformed and locked files, unknown JSON fields, UTF-8 without BOM, file access rules, and temporary-file cleanup.

Live restart validation with a real credential remains pending. The currently stale refresh token may require one final `claude auth login` before the new build can persist the next rotated credential.

## Issue fix validation (#1-#6)

The fixes for GitHub issues #1 through #6 were validated on 2026-07-19:

- `dotnet test UsageBeacon.sln -c Debug`: 50 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug` and `-c Release`: 0 warnings, 0 errors.
- New automated coverage: chained refresh from an expired pending credential, adoption of a replaced on-disk credential, polling-loop survival of subscriber exceptions, cooldown behavior with and without cached usage, executed status-line-bridge forwarding with a quoted path, Codex DTO parsing of missing `resetsAt` and fractional `usedPercent`, and the UI Automation rescan policy.

Manual verification remains pending for: widget placement in every display mode after the UI Automation caching change, live status line forwarding with a real user-configured command, and a live expired-pending-credential renewal.

## Local artifact cleanup

Local generated outputs were cleaned on 2026-07-19. The legacy `TokenChecker/` build tree, project and test `bin/` and `obj/` trees, and non-`latest` publish directories were removed. The only retained executable is `publish/latest/UsageBeacon.exe`. Generated outputs are recoverable by rebuilding; the removed local directories were not versioned repository content.
