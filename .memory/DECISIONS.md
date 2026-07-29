# Decision Log

## D-001: Maintain an independent unofficial fork

- Date: 2026-07-18
- Status: Active
- Decision: Develop the fork independently while preserving respectful upstream attribution and avoiding any implication of endorsement.
- Reason: Upstream contributions were not being accepted, but continued Windows-focused maintenance is desired.
- Consequences: Fork status must remain prominent in the README and attribution documentation. The upstream MIT License remains unchanged.
- Evidence: [`README.md`](../README.md) and [`docs/NOTICE.md`](../docs/NOTICE.md).

## D-002: Rename the product and .NET projects to UsageBeacon

- Date: 2026-07-18
- Status: Active
- Decision: Use `UsageBeacon` for the product, executable, solution, projects, namespaces, application data, startup registration, and primary mutex.
- Reason: The fork needs a distinct identity while continuing the original product direction.
- Consequences: Legacy `TokenChecker` names remain only where required for migration, compatibility, history, and attribution.
- Evidence: [`UsageBeacon.sln`](../UsageBeacon.sln), [`UsageBeacon/UsageBeacon.csproj`](../UsageBeacon/UsageBeacon.csproj), and [`COMPATIBILITY.md`](COMPATIBILITY.md).

## D-003: Keep repository documentation in English

- Date: 2026-07-18
- Status: Active
- Decision: Keep `README.md` at the repository root and place other public documentation under `docs/`. Repository-facing templates and agent guidance are also English.
- Reason: A consistent public language and predictable documentation layout make the fork easier to maintain and contribute to.
- Consequences: The Japanese application UI is not translated by this decision.
- Evidence: [`README.md`](../README.md), [`docs/`](../docs/), and [`.github/`](../.github/).

## D-004: Preserve legacy installations during the rename

- Date: 2026-07-18
- Status: Active
- Decision: Migrate legacy application data and startup registration automatically, fall back safely when data migration is blocked, and hold the legacy mutex.
- Reason: Existing Token Checker for Windows users must not lose settings or accidentally run both applications after upgrading.
- Consequences: Compatibility identifiers cannot be removed as cosmetic leftovers without an explicit migration plan.
- Evidence: [`COMPATIBILITY.md`](COMPATIBILITY.md).

## D-005: Version repository knowledge explicitly

- Date: 2026-07-18
- Status: Active
- Decision: Store non-obvious repository knowledge in `.memory/` and prohibit reliance on unrecorded agent context.
- Reason: Decisions and constraints must survive tool changes, new sessions, and contributor handoffs.
- Consequences: Every change that introduces or invalidates a non-obvious constraint must update `.memory/` in the same work.
- Evidence: [`.memory/README.md`](README.md) and [`.codex/AGENTS.md`](../.codex/AGENTS.md).

## D-006: Prefer Claude Code native rate-limit data

- Date: 2026-07-18
- Amended: 2026-07-19
- Status: Active
- Decision: Prefer the `rate_limits` values delivered to Claude Code status line commands, and retain Anthropic's undocumented OAuth usage endpoint only as a low-frequency fallback.
- Reason: The OAuth usage endpoint applies strict request limits and can remain unavailable while first-party Claude surfaces still display usage. Native rate-limit data arrives with normal Claude Code responses and requires no additional usage request.
- Consequences: Status line integration must be opt-in, preserve and forward an existing command, discard unrelated session metadata, and restore settings only when doing so cannot overwrite later user changes. OAuth fallback must honor server cooldowns. Because refresh tokens can rotate, refreshed credentials must be persisted before restart when the source is a supported local Windows file. Persistence must use typed source metadata, full OAuth-state comparison, unknown-field and access-rule preservation, replacement with backup recovery, and an in-memory pending state after temporary failures. Sources without a safe writer must not be refreshed. When a pending credential expires before persistence succeeds, renewal must continue from the pending rotated token while the on-disk state still matches the pending original; a changed on-disk state supersedes the pending update. WSL credentials must be read by bounded `wsl.exe` calls per distribution so each distribution resolves its own `HOME`; UNC credential paths are prohibited because their existence and read operations cannot be cancelled reliably. Standard output may contain credentials and must never be logged. The status line bridge must forward a preserved command through `cmd.exe` so its Windows semantics are kept regardless of which shells are on `PATH`.
- Evidence: [`docs/CLAUDE_USAGE.md`](../docs/CLAUDE_USAGE.md), [`UsageBeacon/Services/ClaudeCredentialFileStore.cs`](../UsageBeacon/Services/ClaudeCredentialFileStore.cs), [`UsageBeacon/Services/ClaudeStatusLineIntegration.cs`](../UsageBeacon/Services/ClaudeStatusLineIntegration.cs), and [`UsageBeacon/ViewModels/UsageViewModel.cs`](../UsageBeacon/ViewModels/UsageViewModel.cs).

## D-007: Use runtime localization with English as the neutral language

- Date: 2026-07-18
- Status: Active
- Decision: Store user-facing text in .NET resource files, support English and Japanese runtime switching, follow a supported Windows UI language by default, and fall back to English for unsupported languages.
- Reason: A fixed Japanese interface and mixed-language production source limited accessibility and made additional translations expensive.
- Consequences: Production C# and XAML must not contain translated UI literals. Source comments and diagnostics remain English. Every translation must contain the same keys and preserve format placeholders. A new language requires one resource file and one language catalog entry.
- Evidence: [`docs/LOCALIZATION.md`](../docs/LOCALIZATION.md), [`UsageBeacon/Localization/LocalizationService.cs`](../UsageBeacon/Localization/LocalizationService.cs), and [`UsageBeacon/Resources/Strings.resx`](../UsageBeacon/Resources/Strings.resx).

## D-008: Keep the notification-area icon static

- Date: 2026-07-19
- Status: Active
- Decision: Use the packaged UsageBeacon icon in the Windows notification area instead of rendering Claude and Codex utilization as two dynamic bars.
- Reason: The dynamic bar icon was unreliable and duplicated usage information already available in the taskbar widget, popup, and tray tooltip.
- Consequences: The tray icon must continue to provide popup access, localized commands, usage tooltip text, and exit control, but it must not be regenerated when usage changes.
- Evidence: [`UsageBeacon/App.xaml.cs`](../UsageBeacon/App.xaml.cs) and [`docs/CHANGELOG.md`](../docs/CHANGELOG.md).

## D-009: Provide runtime light and dark themes via per-window brush overwrite

- Date: 2026-07-20
- Status: Active
- Decision: Resolve the theme preference (System, Light, Dark) in a static `ThemeService` that mirrors `LocalizationService`, and re-theme each window by overwriting its own resource brushes from code instead of swapping application-level resource dictionaries.
- Reason: The popup already composes its surface color with the transparency setting at runtime, which a static dictionary cannot express, and only the popup and login windows are themed. The System option reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` (missing or unreadable values fall back to light) and re-resolves on `SystemEvents.UserPreferenceChanged` for the General category, raising `ThemeChanged` only when the effective appearance flips.
- Consequences: The taskbar widget intentionally stays taskbar-blended dark and does not follow the app theme. Accent colors (`#0078D4`, Claude `#D07A42`, Codex `#7A8EE0`) and the semantic green/amber/red utilization colors are theme-invariant. `UsageBar` resolves `UsageTrackBrush` from the hosting window and must not declare a local default, because a nearer resource dictionary would shadow the theme-swapped brush. The popup ComboBox chrome and dropdown are re-templated with theme brushes because the default template paints a system-colored (light) chrome that made dark mode unreadable; the dropdown surface uses the opaque `MenuBg` brush since it floats over arbitrary desktop content. Tests that mutate the process-global `ThemeService` must share the `ThemeServiceState` xUnit collection and restore the System preference.
- Evidence: [`UsageBeacon/Services/ThemeService.cs`](../UsageBeacon/Services/ThemeService.cs), [`UsageBeacon/Views/UsagePopupWindow.xaml.cs`](../UsageBeacon/Views/UsagePopupWindow.xaml.cs), and [`UsageBeacon/Views/LoginWindow.xaml.cs`](../UsageBeacon/Views/LoginWindow.xaml.cs).

## D-010: Estimate dashboard costs from local session logs

- Date: 2026-07-20
- Amended: 2026-07-27
- Status: Active
- Decision: Compute the usage dashboard's token totals and USD estimates by parsing the local Claude Code transcript files (`~/.claude/projects/**/*.jsonl`) and Codex rollout files (`~/.codex/sessions/**/*.jsonl`), priced against an embedded per-model table with an optional user override file.
- Reason: The providers only expose utilization percentages; neither vendor offers a server-side history or cost API for subscription accounts. The session logs both CLIs already write are the only local source of per-request token counts.
- Consequences: Codex usage must be derived from deltas of consecutive cumulative `total_token_usage` values (summing `last_token_usage` over-counts, verified ~+3.7% on real data; a negative delta means a baseline reset). Malformed counters, wrong JSON types, fractional values, negative values, overflows, and cached counts greater than inclusive input counts invalidate only that line and must not advance the Codex baseline. Vendor token semantics must stay normalized at parse time: OpenAI `input_tokens` includes `cached_input_tokens` and `output_tokens` includes reasoning tokens, while Anthropic buckets are disjoint. Claude assistant records repeat within a file and are deduplicated by message id + request id; `<synthetic>` models are skipped. The parse cache (`%LOCALAPPDATA%\UsageBeacon\insights-cache.json`) retains entries of deleted files because Claude Code prunes transcripts after ~30 days, making the cache the primary history source; retention is bounded by D-011 and the word "retains" means "within that window". It stores only numeric usage, timestamps, model names, and file paths — never message content — and the pipeline performs no network access. Pricing matches exactly, then by longest key prefix at a `-` boundary only (so `gpt-5` never claims `gpt-5.5`); after model resolution, the latest rate whose UTC effective time is at or before the usage timestamp applies. A legacy single-object override remains timeless, while an override array replaces the model's complete schedule. Unknown models are excluded from totals and surfaced in the UI rather than guessed. The per-model table uses `ItemsControl`, not `DataGrid`, because the default DataGrid chrome cannot follow D-009 theming.
- Evidence: [`UsageBeacon/Services/Insights/`](../UsageBeacon/Services/Insights/), [`UsageBeacon/ViewModels/DashboardViewModel.cs`](../UsageBeacon/ViewModels/DashboardViewModel.cs), [`UsageBeacon/Views/DashboardWindow.xaml.cs`](../UsageBeacon/Views/DashboardWindow.xaml.cs), and [`docs/DASHBOARD.md`](../docs/DASHBOARD.md).

## D-011: Bound the insights cache by dropping entries, not by compacting them

- Date: 2026-07-27
- Status: Active
- Decision: Drop cached `TokenUsageEntry` values older than 180 days at the end of every dashboard scan, keeping the per-file record itself. Reject the lossy alternative of compacting old entries into per-day, per-model totals.
- Reason: D-010 retains entries of deleted files indefinitely, which has no bounded steady state. A lossy rollup was designed first and rejected during review: it required a monotonic local-day watermark that forward clock skew (dead RTC, bad NTP sync, restored VM snapshot, mistyped date) could raise permanently, after which every later scan would silently discard legitimate in-window entries with no way to recover; a build downgrade that dropped the watermark would have reparsed still-present logs into raw entries that could not deduplicate against the synthetic rollup identities, double counting totals. It would also have bet irreversibly on `ModelPricingCatalog.TryGetCost` staying linear per token bucket, because once rows are rolled up the raw counts needed to reprice them are gone.
- Consequences: The cutoff is compared against UTC timestamps because 180 days dwarfs any time zone offset, so the cache needs no `TimeZoneInfo`. The window sits far outside the 30-day aggregation window, so pruning can never change a displayed figure. The `CachedFile` record is kept with its original length and write time even when all of its entries are dropped, because removing the key would make `GetEntries` reparse a file that may be hundreds of megabytes on every scan and discard the whole result; the surviving shells are bounded by the number of log files ever seen. Pruning is idempotent, and a modified file that is later reparsed simply re-ingests raw entries that the existing `IdHash` deduplication handles, so there is no double-count path. No schema version change and no new JSON fields, so cache files stay compatible in both directions. Restoring a longer-than-30-day view later means revisiting this decision, not reviving the rollup design.
- Evidence: [`UsageBeacon/Services/Insights/UsageLogCache.cs`](../UsageBeacon/Services/Insights/UsageLogCache.cs), [`UsageBeacon/ViewModels/DashboardViewModel.cs`](../UsageBeacon/ViewModels/DashboardViewModel.cs), and [`docs/DASHBOARD.md`](../docs/DASHBOARD.md).

## D-012: Log crashes locally and redacted, with no telemetry

- Date: 2026-07-27
- Status: Active
- Decision: Write unhandled exceptions to a rotating, size-capped `crash.log` under `%LOCALAPPDATA%\UsageBeacon\logs`. Crash records only, always enabled, never transmitted, and with no settings surface.
- Reason: The error dialog deliberately shows only `ex.Message` because a full stack exposes local paths, which left no diagnosable record anywhere. Two of the four fault channels were also unwired: `AppDomain.UnhandledException` covers the single-instance and settings work that runs before `DispatcherUnhandledException` is attached, and `TaskScheduler.UnobservedTaskException` backstops fire-and-forget refreshes whose `RefreshCoreAsync` has no catch-all of its own. The bug report template already asked contributors for sanitized logs that did not exist.
- Consequences: Nothing leaves the machine, so the no-telemetry statement in the README still holds. Every record is redacted before it reaches disk: the user profile directory in both separator forms, the account name matched at word boundaries only (a substring replace would corrupt stack frames for ordinary short account names), and API-key, bearer, JWT, and `key=value` credential shapes. Redaction regexes carry timeouts. A timeout must fail closed by writing only a fixed omission reason, exception type, timestamp, and application version; the original rendered record, source tag, message, and stack must not be written. `Write` still swallows every writer failure because diagnostics must never mask the exception being reported. The writer appends rather than using the tmp-and-move idiom the other stores use, since a dying process is better served by a partial line than by a lost record; that difference is intentional. The constructor creates no directory, so a healthy install never grows a logs folder. `SetObserved` is not called on unobserved task exceptions, keeping the logging behavior-neutral. `%LOCALAPPDATA%\UsageBeacon` is now a documented second data location that uninstall instructions must cover.
- Evidence: [`UsageBeacon/Services/CrashLogWriter.cs`](../UsageBeacon/Services/CrashLogWriter.cs), [`UsageBeacon/App.xaml.cs`](../UsageBeacon/App.xaml.cs), and [`.github/ISSUE_TEMPLATE/bug_report.yml`](../.github/ISSUE_TEMPLATE/bug_report.yml).

## D-013: Build and publish releases from CI, gated on the tag

- Date: 2026-07-27
- Status: Active
- Decision: Run build and test on every push to `main` and every pull request, and publish releases from a tag-triggered workflow that verifies the tag against `<Version>`, produces the self-contained single-file executable, and attaches it with a SHA-256 checksum.
- Reason: v1.0.0 was built, tested, published, and uploaded by hand, so nothing tied a release asset to its tagged source. The README already told users to review release "checks" that had never been published, and the pull request template's three commands were enforced only by convention.
- Consequences: Workflows must run on `windows-latest` because both projects target `net8.0-windows`. The release job uses the preinstalled GitHub CLI with the automatic token rather than a third-party action, so no additional secret exists and the supply-chain surface stays minimal. The tag must equal the project version with a leading `v`, and the workflow fails before building on a mismatch; `ReleaseMetadataTests` enforces the matching invariant between `<Version>` and the newest `docs/CHANGELOG.md` heading locally. Warnings are not treated as errors, so the "0 warnings" expectation stays a convention verified by review rather than by the build. The procedure lives in `docs/RELEASE.md`; changing the asset name would break the checksum instructions in the README.
- Evidence: [`.github/workflows/ci.yml`](../.github/workflows/ci.yml), [`.github/workflows/release.yml`](../.github/workflows/release.yml), [`docs/RELEASE.md`](../docs/RELEASE.md), and [`UsageBeacon.Tests/ReleaseMetadataTests.cs`](../UsageBeacon.Tests/ReleaseMetadataTests.cs).

## D-014: Roll back failed settings and expose the widget as a button

- Date: 2026-07-29
- Status: Active
- Decision: Treat settings persistence and startup registration as transactional UI operations, and expose the taskbar widget through standard WPF button semantics.
- Reason: Previously the view model accepted a changed value before a failed file or registry write and silently kept the misleading UI state. The widget was a mouse-only `Border`, so keyboard and UI Automation users could not invoke it.
- Consequences: A settings or startup change becomes visible only after the backing operation succeeds; failure restores the previous value and retains a localized inline error until a later successful operation. A malformed settings file is moved to a timestamped `.corrupt-*` backup before defaults can later be saved. The startup abstraction must verify the requested registry state after writing. The widget root must remain a real `Button` with Enter/Space activation, a visible keyboard focus ring, a localized Automation name, and the UIA Invoke pattern.
- Evidence: [`UsageBeacon/ViewModels/UsageViewModel.cs`](../UsageBeacon/ViewModels/UsageViewModel.cs), [`UsageBeacon/Services/AppSettingsStore.cs`](../UsageBeacon/Services/AppSettingsStore.cs), [`UsageBeacon/Services/StartupManager.cs`](../UsageBeacon/Services/StartupManager.cs), and [`UsageBeacon/Views/TaskbarWidget.xaml`](../UsageBeacon/Views/TaskbarWidget.xaml).
