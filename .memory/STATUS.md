# Current Repository Status

Last verified: 2026-07-29

## Git and naming state

- The active development branch is `main`.
- The `fork` remote points to `https://github.com/kmch4n/UsageBeacon.git`.
- The `origin` remote points to `https://github.com/satonico/Token-Checker-win`.
- Product, solution, projects, namespaces, and executable have been renamed to UsageBeacon.
- The GitHub repository has been renamed to `kmch4n/UsageBeacon`, matching the clone and release URLs in the README.
- Release v1.0.0 (2026-07-20) is the first fork release. Fork versioning restarts at 1.0.0: the fork diverged from upstream after its v0.2.0, so upstream tags v0.3.0 and v0.4.0 are not ancestors of `main` and continuing that numbering would misrepresent the contents. Repository topics were set on the same date.

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

## Dark mode validation

The runtime light and dark theme support (D-009) was validated on 2026-07-20:

- `dotnet test UsageBeacon.sln -c Debug`: 64 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug` and `-c Release`: 0 warnings, 0 errors.
- Automated coverage: theme preference normalization, `SetTheme` idempotency and event delivery, system-theme change resolution through the `SystemDarkOverride` seam, settings round-trip and legacy default of `appTheme`, and view-model persistence and constructor loading of the theme.

Manual verification remains pending for: visual appearance of the popup and login window in both themes, live switching while the popup is open, following a Windows app-theme change while "System" is selected, and dark-theme contrast at high transparency levels.

## Usage dashboard validation

The usage dashboard (D-010) was implemented on 2026-07-20:

- `dotnet test UsageBeacon.sln -c Debug`: 97 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug` and `-c Release`: 0 warnings, 0 errors.
- Automated coverage: Claude transcript parsing (usage extraction, cache-creation split fallback, synthetic/malformed skips, within-file dedupe), Codex cumulative-delta parsing (over-count guard, baseline reset, model tracking), pricing resolution (exact, dash-boundary prefix, unknown models, override merge, cost arithmetic), local-day bucketing and 7/30-day windows, cross-file dedupe determinism, cache reuse/invalidation/retention-of-deleted-files/corruption/schema-version handling, and end-to-end view-model aggregation over temp log directories.

Manual verification remains pending for: dashboard visuals in both themes and languages, and refresh behavior while logs are being written.

A three-agent verification pass on 2026-07-20 confirmed measurement accuracy empirically: Codex reader sums matched the final cumulative `total_token_usage` of three large real rollout files exactly; Claude 30-day per-model costs matched ccusage to the cent for opus/haiku models (Codex-side deltas vs ccusage stem from ccusage excluding reasoning tokens from output — our counts match the session files and OpenAI billing semantics); cold scan of the ~1 GB corpus took ~3.1 s. Fixes applied from the review: per-file error isolation in the scan, atomic cache save, case-insensitive cache reload, `gpt-5.1-codex-mini` and `gpt-5.4` price entries, and `claude-sonnet-5` at the official introductory price.

The `claude-sonnet-5` price change is now scheduled in `UsageBeacon/Resources/model-pricing.json`: usage through 2026-08-31 keeps the introductory $2/$10 rate (cache 2.50/4/0.20), and usage from 2026-09-01 UTC uses the standard $3/$15 rate (cache 3.75/6/0.30).

Claude Opus 5 pricing support was added on 2026-07-29:

- Local Claude Code transcripts use the exact model identifier `claude-opus-5`.
- The embedded table now applies Anthropic's standard $5 input / $25 output per million token price and the Opus 4.8-equivalent $6.25 / $10 cache-write and $0.50 cache-hit rates.
- `ModelPricingCatalogTests` reads the real embedded pricing source file and guards the model identifier, rates, pricing date, and full-bucket cost calculation.
- `dotnet test UsageBeacon.sln -c Debug`: 134 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Debug --no-restore` and `-c Release --no-restore`: 0 warnings, 0 errors.

## Reliability and accessibility hardening

The prioritized hardening pass was implemented on 2026-07-29:

- The CI-sensitive OAuth file test compares normalized DACL semantics instead of unstable SDDL text, and credential replacement relies on `File.Replace` to preserve the destination DACL.
- Crash-log redaction fails closed on regex timeout.
- Claude and Codex JSONL readers reject wrong types and invalid counters per line; rejected Codex lines do not advance the cumulative baseline.
- WSL credential discovery uses timeout-bounded `wsl.exe` calls per distribution, drains both output streams, kills timed-out process trees, and never performs UNC credential-file reads.
- Pricing schedules select rates using each usage event's UTC timestamp while preserving legacy single-object overrides.
- Settings and startup changes roll back on failure and show a localized inline error. Malformed settings are backed up before defaults can overwrite them.
- The taskbar widget is a keyboard-focusable Button with Enter/Space activation, visible focus, localized UI Automation naming, and Invoke support.
- `dotnet build UsageBeacon.sln -c Debug --no-restore`: 0 warnings, 0 errors.
- `dotnet build UsageBeacon.sln -c Release --no-restore`: 0 warnings, 0 errors.
- `dotnet test UsageBeacon.sln -c Debug --no-build --no-restore`: 160 passed, 0 failed.
- GitHub Actions CI run `30459273684` passed on the hosted Windows runner after normalizing exact duplicate ACEs that do not change effective DACL access.

Manual verification remains pending for the localized settings error presentation, taskbar keyboard focus behavior in the live shell, and WSL discovery against multiple installed distributions.

## Backend hardening validation

A backend-only hardening pass (no UI changes) was completed on 2026-07-27:

- `dotnet test UsageBeacon.sln -c Debug`: 133 passed, 0 failed (97 before the pass).
- `dotnet build UsageBeacon.sln -c Debug` and `-c Release`: 0 warnings, 0 errors.
- Fixed defect: the dashboard scan aborted entirely when any log subdirectory was unreadable, because `Directory.EnumerateFiles` with a `SearchOption` overload uses `EnumerationOptions.Compatible` (`IgnoreInaccessible = false`) and raises the failure from `MoveNext`, outside the guard that only wrapped the enumerator's creation. Reproduced with a deny ACE before the fix and confirmed skipped after it. The call now passes explicit `EnumerationOptions`, and `ResilientFileEnumeration` guards the residual mid-iteration `IOException` class.
- Cache retention (D-011): entries older than 180 days are dropped after each scan.
- Crash logging (D-012): verified against the built assembly by writing a real exception whose message embedded the profile path and an `sk-ant-` key; the record contained `%USERPROFILE%` and `<redacted>` and neither original value.
- CI and release automation (D-013): both workflow files parse, and the tag/version gate was dry-run locally against `UsageBeacon.csproj` (`1.0.0` matches tag `v1.0.0`).

Insights pipeline measured on 2026-07-27 against the real corpus (Claude 136 files / 276 MB, Codex 244 files / 833 MB) by invoking `DashboardViewModel` directly from the Debug build:

- Cold scan 9.50 s, warm scan 0.20 s, cache file 5.55 MB.
- The earlier "~3.1 s cold scan" figure was measured differently and is not comparable; treat 9.50 s as the current baseline for a Debug build with this corpus.
- The 5.55 MB cache confirms that unbounded growth was not yet material. The 180-day window is a cheap safety valve rather than an urgent fix, and the constant can be revisited against this number.

Manual verification remains pending for: dashboard visuals in both themes and languages, refresh behavior while logs are being written, a real crash producing `%LOCALAPPDATA%\UsageBeacon\logs\crash.log` in the running application, and a live run of the release workflow against a throwaway tag.

## Local artifact cleanup

Local generated outputs were cleaned on 2026-07-19. The legacy `TokenChecker/` build tree, project and test `bin/` and `obj/` trees, and non-`latest` publish directories were removed. The only retained executable is `publish/latest/UsageBeacon.exe`. Generated outputs are recoverable by rebuilding; the removed local directories were not versioned repository content.

## Static notification-area icon validation

The dynamic usage-bar tray icon was removed on 2026-07-19 while retaining the tray menu, popup access, localized tooltip, and exit control. Validation completed with 37 passing tests, warning-free Debug and Release builds, a self-contained win-x64 single-file publish, and a successful startup from `publish/latest/UsageBeacon.exe` using the packaged static icon.
