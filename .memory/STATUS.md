# Current Repository Status

Last verified: 2026-07-18

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
