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
- Consequences: Status line integration must be opt-in, preserve and forward an existing command, discard unrelated session metadata, and restore settings only when doing so cannot overwrite later user changes. OAuth fallback must honor server cooldowns. Because refresh tokens can rotate, refreshed credentials must be persisted before restart when the source is a supported local Windows file. Persistence must use typed source metadata, full OAuth-state comparison, unknown-field and access-rule preservation, replacement with backup recovery, and an in-memory pending state after temporary failures. Sources without a safe writer must not be refreshed. When a pending credential expires before persistence succeeds, renewal must continue from the pending rotated token while the on-disk state still matches the pending original; a changed on-disk state supersedes the pending update. The status line bridge must forward a preserved command through `cmd.exe` so its Windows semantics are kept regardless of which shells are on `PATH`.
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
