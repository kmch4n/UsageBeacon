# Current Repository Status

Last verified: 2026-10-06

## agy console flash fix (2026-10-06, unreleased)

- Symptom: with `showAgyUsage` on, a console window flashed periodically. Process tracing against agy 1.3.0 showed `agy -p /usage` spawning `agy --bg-updater`, which ran `agy --version` with its own conhost and opened Windows Terminal; agy logs showed the updater every 15 minutes (every third 5-minute poll). UsageBeacon's own `--version` check runs only once per agy executable write time.
- Fix: `AgyUsageProvider.CreateStartInfo` sets `AGY_CLI_DISABLE_AUTO_UPDATE=true`. A traced run with that value logged "Auto-update disabled via environment variable" and spawned no updater or console; with `1` the updater still ran.
- The maintainer turned `showAgyUsage` off as a workaround until a build with this fix is running.

## Release v1.5.0 validation (2026-10-03)

- Locally, Debug and Release each passed 336 tests and both builds passed with `-warnaserror` before tagging.
- CI run 37109521327 on `main` succeeded before the tag was pushed. Release run 37109640713 for `v1.5.0` succeeded and published https://github.com/kmch4n/UsageBeacon/releases/tag/v1.5.0 (not a draft or prerelease) with the curated notes.
- Both assets were attached: `UsageBeacon.exe` (162,352,322 bytes) and `UsageBeacon.exe.sha256`. The published checksum file's content equals GitHub's asset digest, `b9f23f2ece292d5bd3081d93e849a13f50b86a092c08c30d36f8775e7122ac95`.
- `publish/latest/UsageBeacon.exe` was republished locally from the release commit (`1.5.0+bdca14f`, SHA-256 `539161ceec46bf24867721de126b3f5ad4bd49aa223406b7b5e31f1a8b27b6af`; local builds are not byte-identical to CI) and restarted.

## Antigravity dashboard estimates (2026-10-03, released in v1.5.0)

- The dashboard now prices Antigravity CLI transcripts (D-019), and the table adds `gpt-6-luna`, Gemini 3.1 Pro and 3.6-3.8 Flash, and Claude Opus/Sonnet 5.5 (`asOf` 2026-10-03).
- Live check with the maintainer's logs: today's Antigravity estimate appeared in every card, the chart legend and bars, and the selected day; Gemini, Opus 5.5, and `gpt-6-luna` left the unpriced-model notice. `qwen3.5-uncensored-4b` remains unpriced.
- Resolved the same day at the maintainer's request: the `gpt-5.6-sol`/`terra`/`luna` rates were aligned with OpenAI's pricing page ($4/$20, $2/$12, $0.20/$1.20; previously $5/$30, $2.50/$15, $1/$6). Retained history is repriced; no effective-dated schedule was added because the change date is unknown.
- Debug and Release each passed 335 tests and both builds passed with `-warnaserror`. `publish/latest/UsageBeacon.exe` was republished from the working tree before commit (SHA-256 `cd1e606cf86bdb4ec9b632740897d2edf0074c3b720fe7979f46bbb8f691a19e`) and restarted. Superseded by the v1.5.0 publish below.

## Release v1.4.0 validation (2026-10-03)

- `feature/agy-usage` was fast-forwarded into `main`. The version is 1.4.0, `docs/CHANGELOG.md` has the dated heading, and `docs/releases/v1.4.0.md` holds the curated notes.
- Locally, Debug and Release each passed 318 tests and both builds passed with `-warnaserror` before tagging.
- CI run 37105203159 on `main` succeeded before the tag was pushed. Release run 37105301842 for `v1.4.0` succeeded on every step and published https://github.com/kmch4n/UsageBeacon/releases/tag/v1.4.0 (not a draft or prerelease) with the curated notes.
- Both assets were attached: `UsageBeacon.exe` (162,335,938 bytes) and `UsageBeacon.exe.sha256`. The published checksum file's content equals GitHub's asset digest, `afd46879b8a1dd8b1acf121ac223e04db8b08068777f7924af2290feeba22591`.
- `publish/latest/UsageBeacon.exe` was republished locally as 1.4.0 from the release commit's tree and restarted.
- The CI annotation that `actions/checkout@v4` and `actions/setup-dotnet@v4` target the deprecated Node.js 20 runtime remains unaddressed; the runs still succeed.

## Antigravity Gemini quota (2026-10-03)

- An earlier attempt on `feature/agy-usage` by another agent was partly discarded: its dashboard transcript pricing (`AgyTranscriptReader`, aggregator and dashboard changes), the Google "G" image that had also replaced the dashboard window and title-bar icon, and its views that referenced a nonexistent `AgyTodayCostUsd` and did not compile. The provider, parser, settings flag, and view model polling were kept and revised (D-018).
- Live check against agy 1.2.16: the report returned in about 6 seconds with `num_turns: 0` and an empty `conversation_id`. The popup showed "Antigravity (Gemini)" with five-hour 100% and weekly 17%, and the widget showed the Gemini pair after Codex. README screenshots were regenerated for v1.4.0 (see "README screenshots").
- Debug and Release each passed 318 tests; both builds passed with `-warnaserror`. The self-contained build was republished to `publish/latest/UsageBeacon.exe` (SHA-256 `badedecfa8a988684667ed2ff803af583e7b056624548fe3ee4d1a40af7054be`, built from the uncommitted working tree immediately before this change's commit) and started. The publish recorded in the next section is superseded.
- Pending: `publish/v1.2.0-local/` (created 2026-09-25) predates this work and was left in place for the maintainer to remove.

## Dashboard caption buttons (2026-10-03)

- The dashboard's custom title bar keeps `WindowStyle="None"` with `WindowChrome`. Its minimize, maximize, and close buttons now use the standard Windows caption layout: 46 DIP wide, full title-bar height, square corners, Segoe Fluent Icons glyphs with Segoe MDL2 Assets as the Windows 10 fallback (`E921`, `E922`/`E923`, `E8BB`), a theme-gray hover, and a `#C42B1C` close hover with a white glyph.
- A maximized borderless window extends past the monitor work area by its resize frame. On the primary 150% monitor this was 10 physical pixels and clipped the close button. `Utilities/MaximizedWindowInsets` measures the actual HWND and monitor work area, and the dashboard applies that overhang as `RootGrid.Margin` while maximized; it also keeps the window off a top-docked taskbar. A live probe showed the close button ending exactly at the monitor's right edge after the fix.
- Debug and Release each passed 297 tests; both builds passed with `-warnaserror`. Keyboard focus uses WPF's default focus visual, as other dashboard buttons do.
- The self-contained build was republished to `publish/latest/UsageBeacon.exe` (SHA-256 `c9d583bcf45f9f9211fea7213b2ff950142937df48af54e9f23f72b6c78d6cce`, built from the working tree immediately before this change's commit) and the running instance was restarted from it. The publish recorded in the next section is superseded.

## Official service images in popup and dashboard (2026-10-03)

- The usage popup headers now show the bundled `claude.png` and `openai.png` instead of the `✦` and `▶` text stand-ins. No new image assets were added; provenance in `docs/NOTICE.md` now covers the widget, popup, and dashboard.
- The dashboard defines `ClaudeIcon` and `CodexIcon` as window-level `BitmapImage` resources. They appear in the lifetime and period cost breakdowns (icon plus amount instead of "Claude $x · Codex $y"), beside the cost chart legend swatches, and in the model table service column. The legend swatches remain because the bar colors are the data encoding.
- The cost breakdown icons are decorative; each amount's `AutomationProperties.Name` keeps the service name for assistive technology.
- WPF renders on dark backgrounds were inspected from an offscreen capture with synthetic usage. README screenshots were not regenerated.
- Debug and Release each passed 295 tests; both solution builds passed with `-warnaserror`, zero warnings and errors. The self-contained build was republished to `publish/latest/UsageBeacon.exe` (SHA-256 `fc87ea416b87c591baf2426df4021efc3a622cb10d6cbb6186767c1da08cd56e`, built from the uncommitted working tree immediately before this change's commit) and the running instance was restarted from it.

## Balanced widget padding (2026-10-02)

- The measured widget content now has 10 DIP of horizontal padding on both sides across all display modes. A WPF layout regression test checks the visible bounds for both the default and weekly layouts.
- The README widget screenshot was regenerated from the current WPF view using synthetic utilization values. A live capture on the primary 150% DPI taskbar shows modest left and right spacing, with the widget still inside the taskbar.
- Debug and Release each passed 294 tests; both solution builds passed with `-warnaserror`, zero warnings and errors. Live behavior on other DPI combinations remains unverified.

## Taskbar-only widget placement correction (2026-10-02)

- The desktop retreat was an intentional fallback in `TaskbarWidget.ApplyLayout`, `RetreatOutsideTaskbar`, and `PositionAtScreenEdge`, triggered by insufficient measured width or unavailable taskbar geometry. It is now removed. The widget hides when no safe taskbar slot or notification-area bounds can be verified, and the one-second layout refresh restores it when space returns. Its actual HWND rectangle must fit inside the taskbar before it is shown.
- A follow-up screenshot exposed a large empty hover surface on the left: the first correction right-aligned content inside a fixed 240 DIP window. The window now measures its current text and images for wide, compact, and vertical modes, and repositions when labels change. The separate 26 physical pixel notification-area clearance remains to protect nearby system icons.
- On the primary 150% DPI monitor, the first measured-width build was hidden because its actual HWND was one physical pixel inside the 26-pixel clearance. `EnsureNotificationClearance` now corrects the measured overflow using actual HWND bounds, then verifies containment and clearance. The live widget is visible inside the taskbar, with exactly 26 physical pixels before the notification area. A DPI-aware screen capture confirms the content begins close to the window's left edge and reaches its right edge. Other monitor scale combinations remain unverified.
- The final correction passed 294 tests each in Debug and Release; both builds passed with `-warnaserror`, zero warnings and errors.

## Release v1.3.0 validation (2026-10-01)

- `UsageBeacon/UsageBeacon.csproj` and `docs/CHANGELOG.md` now identify v1.3.0. The README's four screenshots were regenerated from the current WPF views using synthetic usage only; the app icon image has no PNG metadata. `docs/releases/v1.3.0.md` contains the curated release notes.
- A screenshot exposed an initially blank popup language selection. A failing WPF test reproduced it, and the popup now selects the saved language item. The corrected screenshot visibly shows English.
- Debug and Release each passed 291 tests; both solution builds passed with `-warnaserror`, zero warnings, and zero errors. `git diff --check` passed. The local branch matched `fork/main` before release commits, and the configured Git author and GitHub account were both `kmch4n`.
- The release source was committed as `83e816c92d1f87610a974e3390458cff15ed628f` and tagged `v1.3.0`. Main CI run `36864956699` and tag release run `36865229876` both succeeded. The release is at `https://github.com/kmch4n/UsageBeacon/releases/tag/v1.3.0` with `UsageBeacon.exe` and `UsageBeacon.exe.sha256` attached.
- The downloaded release asset's SHA-256 matches both its checksum file and GitHub's asset digest: `ceef016ec3fe6031da8d6f365fc5629a1a71807561339c9488741c93e10eb032`. The same executable was copied to `publish/latest/UsageBeacon.exe` and is the running process; its product version includes `1.3.0+83e816c`. An earlier pre-tag local build had SHA-256 `28bafe4c6d2ac6817fc24a0bdffacaba9acaa8909a333d19e9e9bd84980c2d58` and has been superseded.
- Cleanup of the temporary release-download copies was rejected by automatic execution policy. They remain under the local temporary directory. The older `publish/v1.2.0-local` directory and stale `publish/latest/UsageBeacon.pdb` also remain after earlier policy rejection. The canonical local executable is the one in `publish/latest`.

## Optional weekly widget and official service images (2026-10-01)

- The taskbar widget now has an opt-in weekly setting. Each service keeps its five-hour value on the left and weekly value on the right at the same 14 DIP text size. The default remains off. Missing five-hour data stays separate from weekly data, and a failed settings save restores the accepted checkbox and widget state.
- Bundled official Claude and OpenAI PNGs replace the widget's placeholder symbols across wide, compact, and vertical layouts. Source URLs and ownership are recorded in `docs/NOTICE.md`.
- Independent review found that unknown left-side button boundaries could be mistaken for free space; that case now retreats outside the taskbar. The new checkbox is associated with its localized label for assistive technology.
- At this implementation checkpoint, Debug and Release each passed 290 tests. Both solution builds passed with `-warnaserror`, zero warnings, and zero errors; `git diff --check` passed. Width and resource-loading tests use WPF layout measurements. Live widget appearance and mixed-DPI interaction were not yet verified at that checkpoint.
- At this earlier checkpoint, the self-contained single-file build was republished to `publish/latest/UsageBeacon.exe` on 2026-10-01 at 12:21:15 UTC, SHA-256 `8d3595ea0e7c220fc1d41aaf147f8b2075e03c2dec12e3baf0ae422e1415826d`. The v1.3.0 build above superseded it. The older `publish/latest/UsageBeacon.pdb` and `publish/v1.2.0-local` remain after cleanup was rejected by execution policy.
- Taskbar button boundaries are cached for up to five seconds when the notification-area rectangle is stable. If a button moves during that interval, a temporary overlap remains possible. The native notification-area clearance is still checked every 200 ms.
- Plan: [`docs/superpowers/plans/2026-10-01-widget-weekly-icons.md`](../docs/superpowers/plans/2026-10-01-widget-weekly-icons.md).

## Taskbar widget notification-area overlap (2026-10-01)

- At this earlier checkpoint, the local standalone executable was published to `publish/latest/UsageBeacon.exe` with SHA-256 `2a90d950945a7612bb0879ac1a1d867b237802f0a7af1852a15575a904fdd09f`. It has since been superseded by the build recorded above. The running process used `UsageBeacon/bin/Debug/net8.0-windows/UsageBeacon.exe` at that time; the user later switched to `publish/latest`. README, agent, and localization instructions point local publish commands to `publish/latest`.
- Cleanup of the old `publish/v1.2.0-local` directory and stale `publish/latest/UsageBeacon.pdb` was rejected by the execution policy. They remain locally; the executable in `publish/latest` is the canonical current build. The active Debug process also prevents complete generated-output cleanup without interrupting the user.
- A read-only 100 ms probe observed six consecutive samples with a 22 px overlap between the widget and `TrayNotifyWnd`. The previous placement used a 2 px gap and repositioned every second while reasserting topmost every 200 ms.
- The widget now checks Win32 rectangles before each topmost assertion, reserves 26 physical pixels at the notification-area edge, and moves outside the taskbar when the inline corridor cannot be verified. It rechecks its actual HWND rectangle after moving and hides until a later successful check if clearance cannot be confirmed. A top taskbar uses space below it; a bottom taskbar uses space above it.
- Synthetic placement and geometry tests passed, including a leftward tray expansion, narrow inline slot, top/bottom taskbars, and screen-edge clamping. Debug and Release each passed 281 tests, and both builds passed with zero warnings and errors under `-warnaserror`. Independent plan and final-diff reviews found no remaining confirmed blocker. The running application was not replaced, so live interaction and mixed-DPI placement of this build remain unverified. The Windows notification area can move between 200 ms samples, so a brief overlap is still possible before the next check.
- Plan: [`docs/superpowers/plans/2026-10-01-taskbar-widget-overlap.md`](../docs/superpowers/plans/2026-10-01-taskbar-widget-overlap.md).

## Application icon integration (2026-09-28)

- The transparent source artwork is in [`docs/images/app-icon.png`](../docs/images/app-icon.png), and its multi-size Windows icon is [`UsageBeacon/Resources/tray.ico`](../UsageBeacon/Resources/tray.ico) with 16, 24, 32, 48, 64, 128, and 256 px frames. The user-provided JPEG had a baked-in checkerboard, so it was not used directly as an application asset.
- [`UsageBeacon/UsageBeacon.csproj`](../UsageBeacon/UsageBeacon.csproj) embeds the icon as the executable icon and a WPF resource; [`UsageBeacon/App.xaml.cs`](../UsageBeacon/App.xaml.cs) uses that resource for the notification area. The dashboard window now sets the same icon explicitly for its taskbar entry and displays it in the custom title bar.
- A running copy of an older executable must be restarted or replaced to display the new icon. Windows icon caching may retain an older executable icon until it refreshes; this is distinct from the WPF window icon.
- The source PNG has a real alpha channel, and the ICO frames were inspected at 16 and 32 px on light and dark backgrounds. Debug built with zero warnings and 274 tests passed. Release built with zero warnings and 274 tests passed from a separate output directory because a running UsageBeacon process holds the ordinary Release executable and DLL open. Dashboard window tests instantiate the XAML and assert that the window and title-bar icon sources load. The running application was not interrupted, so its live taskbar rendering remains unverified.

## Additional bug audit before the next release (2026-09-27)

- Regressions covered: raw exception details in dialogs and popup usage errors, Claude transcript files open for writing, Codex app-server initialize timeout recovery and pipe-write cleanup, Claude account switch/sign-out while credentials are cached, caller cancellation, stale startup registration and legacy migration, and pending rotated credential fallback during temporary source read failure.
- Synthetic tests were observed failing before the associated corrections. Full Debug and Release test suites each passed 274 tests with `-warnaserror`; both solution builds passed with zero warnings and errors. `git diff --check` passed. An independent review identified the pending-credential and startup-migration regressions; both were corrected and retested. A follow-up privacy review found no remaining direct exception-detail display path.
- Live Claude/Codex authentication, real concurrent log writing, Windows login startup, and interactive UI behavior remain unverified. Generic popup errors now omit untrusted details; those details are not written to a diagnostic log, which may limit troubleshooting.
- Hosted CI runs `36326012748`, `36326333436`, and `36326579089` exposed a test-harness difference: the fake Codex server's PowerShell script never reached its startup marker. Using an absolute Windows PowerShell path did not resolve it. The fake server now runs entirely in its batch launcher, removing that extra process dependency. CI run `36326769556` passed both builds and all tests at `14b48e9`.
- Six focused code commits through `ed6e1ba` contain these fixes. The accompanying changelog and [`docs/superpowers/plans/2026-09-27-bug-audit-fixes.md`](../docs/superpowers/plans/2026-09-27-bug-audit-fixes.md) record the scope and validation. Existing bug Issues remain open; feature Issues and the next release remain deferred.

## Bug fixes awaiting release (2026-09-27)

- GitHub Issues #17 and #18 were closed as completed after their v1.2.0 release inclusion and 46 focused regression tests passed. Issue #13 remains open because its CI and release workflow exists but its warnings-as-errors acceptance criterion is not configured.
- Fixes for #20 (`13354c5`), #21 (`38623e8`), #22 (`a0fd03e`), and #19 (`711f334`) cover wrong-typed cache JSON, retained dashboard history without source directories, incomplete pricing overrides, and rejected popup picker selections. They shipped in v1.3.0, and the four Issues were closed as completed on 2026-10-04.

## Top-priority Issue fixes (2026-10-04, released in v1.6.0)

- Priority order chosen: #13 CI warnings, #12 popup accessibility, #9 widget tooltip countdown, #7 usage alerts (D-020), #11 hide widget. #8, #14, and #15 were deferred: #8 is partly covered by the rate-limit retry time, #14 adds a security-sensitive network client, and #15 needs external winget/Scoop submissions.
- The widget tooltip is rebuilt on `ToolTipOpening` so the countdown is current; the accessible name keeps the D-017 wording. Hiding the widget is not persisted, and the topmost timer keeps the window hidden while `IsSuppressed` is set.
- Validation: Debug and Release builds with `-warnaserror` had 0 warnings; 349 tests passed in each configuration. In a temporary local build with the threshold lowered to 20%, a real toast appeared with the expected Japanese text, and the widget tooltip showed each period with its countdown. The tray-menu hide/restore and the popup focus ring were not exercised interactively, because automated clicks could not reach the tray overflow icon.

## Release v1.6.0 (2026-10-04)

- Tag `v1.6.0` at `a6eeaf8`: CI run 37174353960 and the release workflow succeeded. The release attaches `UsageBeacon.exe` and `UsageBeacon.exe.sha256`; the downloaded executable hashes to `9bddb95a...77b9`, matching both the checksum file and GitHub's asset digest.
- `docs/images/popup.png` was recaptured with the alert setting row, and `docs/images/widget-tooltip.png` was added, both from the running app with the maintainer's usage. A toast screenshot was not added because no limit had reached 80%.
- `publish/latest` was rebuilt as 1.6.0 from the tag and restarted.

## Issue triage (2026-10-04)

- Closed as completed after checking the source: #19-#22 (fixed in v1.3.0), #16 (crash-only logging under D-012; no opt-in diagnostics mode is planned), and #10 (superseded by the `ShowWeeklyInWidget` setting).
- Still open and valid: #7 (threshold toasts), #8 (only the rate-limit retry time is shown; no general next-fetch display), #9 (the widget tooltip has no reset countdown), #11 (no hide-widget mode), #12 (popup ComboBoxes have no automation names), #13 (CI exists, but builds do not treat warnings as errors), #14 (no release update check), and #15 (no winget/Scoop manifests).
- Tests for the local changes: Debug and Release each passed 253 tests; Debug and Release builds each passed with `-warnaserror`, 0 warnings and 0 errors. WPF UI tests use synthetic cache/settings fixtures. An independent review found no blocking defects; live interaction and rendering remain unverified.
- The #21 empty-state decision currently infers retained usage from the first recorded day, unknown-cost flag, and model rows. Revisit that decision if aggregation adds a new history category that does not set one of these signals.
- Plan: [`docs/superpowers/plans/2026-09-27-open-bug-fixes.md`](../docs/superpowers/plans/2026-09-27-open-bug-fixes.md).

## Release v1.2.0 validation

Release v1.2.0 was published on 2026-09-25 JST (2026-09-24 UTC) from tag `v1.2.0` at `353810e1190d04ae6b32514bdb298be98b93a162`:

- Local Debug and Release test suites each passed 236 tests. A self-contained single-file `win-x64` publish completed and its executable started successfully.
- Main CI run `36026549826` and tag release run `36026838666` both succeeded, including version matching, tests, publish, checksum generation, and release creation.
- The published release contains curated notes, `UsageBeacon.exe`, and `UsageBeacon.exe.sha256`. The checksum file matches GitHub's executable digest `0d0420f724b43b7f0e097ec276be3dc7d83e50938bfe2e342c2ec48752b92954`.
- README dashboard screenshots were captured from the actual WPF view with illustrative data. They contain no personal usage history, account identifiers, or text/EXIF metadata. An independent release review found two documentation/visual issues; the dashboard scrollbar and release procedure were corrected before publication.
- Issues #17 and #18 were included in the release but their GitHub Issue states were not changed. The Actions runs emitted a Node.js 20 deprecation notice for `actions/checkout@v4` and `actions/setup-dotnet@v4`; it did not fail either run.

## Issues 17 and 18 local validation (2026-09-12)

- At this 2026-09-12 local validation, the full-response HTTP limits (D-016) and structured crash redaction (D-012 amendment) were uncommitted and unreleased. They were subsequently released in v1.2.0; Issue states were not changed.
- Red phase: 12 expected HTTP/redaction failures, a separate view-model body-timeout failure, then three additional structured-value failures. A final punctuation case caught a regression during self-review before it was corrected.
- Final `dotnet test UsageBeacon.sln -c Debug --no-restore`: 210 passed, 0 failed, 0 skipped (26 added cases over the prior 184).
- Final Debug and Release builds: 0 warnings, 0 errors. `git diff --check` passed.
- Tests use synthetic HTTP responses, credentials, and temporary directories; no live provider or real credential validation was performed. Existing timeout-redaction omission and other logger tests remain passing.
- Independent review was requested but could not run because of an execution-service usage limit. The implementation received a local self-review; independent review remains pending.
- Generated `UsageBeacon/bin`, `UsageBeacon/obj`, `UsageBeacon.Tests/bin`, and `UsageBeacon.Tests/obj` remain locally because execution policy rejected cleanup, including a retry with explicit verified workspace paths. `publish/latest` was not modified.
- Plan and scope: [`docs/superpowers/plans/2026-09-12-issues-17-18.md`](../docs/superpowers/plans/2026-09-12-issues-17-18.md).

## Git and naming state

- The active development branch is `main`.
- The `fork` remote points to `https://github.com/kmch4n/UsageBeacon.git`.
- The `origin` remote points to `https://github.com/satonico/Token-Checker-win`.
- Product, solution, projects, namespaces, and executable have been renamed to UsageBeacon.
- The GitHub repository has been renamed to `kmch4n/UsageBeacon`, matching the clone and release URLs in the README.
- Release v1.0.0 (2026-07-20) is the first fork release. Fork versioning restarts at 1.0.0: the fork diverged from upstream after its v0.2.0, so upstream tags v0.3.0 and v0.4.0 are not ancestors of `main` and continuing that numbering would misrepresent the contents. Repository topics were set on the same date.

Remote facts are drift-prone and must be verified with `git remote -v` before relying on them.

## Release v1.1.0 validation

Release v1.1.0 was published on 2026-07-31 (2026-07-30 UTC), the first release produced by the tag workflow rather than by hand:

- Pre-tag local checks: `dotnet test UsageBeacon.sln -c Debug` 184 passed, 0 failed; `dotnet build` in Debug and Release, 0 warnings, 0 errors.
- `.github/workflows/release.yml` run 30562112449 succeeded on every step, including the tag/version gate.
- Both assets were attached: `UsageBeacon.exe` (161,926,378 bytes) and `UsageBeacon.exe.sha256`.
- Checksum verified end to end: the published `UsageBeacon.exe.sha256` content equals GitHub's own asset digest, `db4c962475f43e1f1618d8390c5400b7fcebe5e6f0236a4ebf04eb26e65ee185`. The verification command documented in the README therefore matches.
- `docs/CHANGELOG.md` needed three entries that the lifetime-usage commits had not recorded (lifetime cost card, archived retention plus parser-revision cache invalidation, and the Codex active-session and pre-`turn_context` attribution fixes). Changelog entries must be written in the same commit as the change; the release step is too late to reconstruct them reliably.

Pending: `publish/latest/UsageBeacon.exe` is still 1.0.0. The application was running during the release, which locks that path, so refreshing the persistent local executable requires exiting UsageBeacon first.

## README screenshots

`README.md` embeds PNGs under `docs/images/`.

Superseded 2026-10-03: at the maintainer's request, the widget, popup, and both dashboard images
were recaptured from the running `publish/latest` app with the maintainer's real usage and no
masking. The capture used a temporary English UI, 0% popup transparency, and USD currency, and the
user's settings were restored afterward. The popup's 7 px outer margin was cropped and its rounded
corners made transparent; the widget shows the real taskbar background behind it. The privacy
rule below no longer applies to these images; the other capture notes still do.

Original 2026-10-01 record: On 2026-10-01, the widget, popup, and both dashboard
images were recaptured from the current WPF views with illustrative data in separate screenshot
processes:

- None of the four screenshots contains personal usage data. The widget and popup use synthetic
  rate-limit snapshots; the dashboard uses synthetic daily usage. Future replacements should
  preserve this property rather than relying on blur.
- Screenshots use the English UI because repository documentation is English (D-003). The separate
  screenshot processes set English without changing the user's app language preference.
- The popup composes its surface with the transparency setting (D-009), so any non-zero transparency
  bleeds the background into the image. Set transparency to 0% while capturing.
- The widget and popup captures render the WPF content at 150% bitmap scale against a controlled
  background. The dashboard captures render the full WPF window with a synthetic `DashboardData`.
  They do not include other desktop windows, tray icons, or credentials.
- The screenshot harness uncovered a blank initial language selection in the popup. A regression
  test and fix now select the saved language item before the image was finalized.

`docs/NOTICE.md` and `README.md` both link to `https://github.com/satonico/Token-Checker`, which
returned HTTP 404 on 2026-08-16. Upstream's own README carries the same dead link, so the repository
appears to have been removed or made private rather than the reference being wrong. The name
`satonico224` matches the copyright holder in both the upstream and local `LICENSE`. The link is kept
because it is still the canonical reference for the original work; changing it touches D-001
attribution and should be a deliberate decision across both files.

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

## Locally retained lifetime usage

The dashboard lifetime-cost implementation was revised on 2026-07-30:

- The card reports total API-price-equivalent USD with a Claude/Codex split and the earliest retained local day. It is labeled as locally recorded history and warns that deleted-before-scan logs or cache loss can leave gaps.
- Detailed entries remain associated with their files for 180 days. Older events move to a path-independent schema-v2 archive that keeps the exact timestamp, service, model, five token buckets, and identity hash, allowing historical repricing and future-record exclusion. Archived model names are table-encoded and events use compact positional JSON rows.
- Schema-v1 token-only archives are reparsed once. Recovered identities become exact v2 events; unrecoverable totals remain explicitly unpriced. The dashboard now uses a written coverage note instead of a `+` suffix or fabricated service split.
- Cache saves are dirty-only and stream JSON directly to the temporary file before replacement.
- Automated coverage includes Claude/Codex lifetime separation, all token price buckets, exact effective-time boundaries, later pricing of archived unknown models, future archived events, v1 recovered and unrecoverable migration, deduplication, large histories, and dirty-only saving.
- The real migrated history exposed `gpt-5.2-codex` as the only unknown model. Its official $1.75 input / $0.175 cached input / $14 output rates were added to the embedded catalog.
- `dotnet test UsageBeacon.sln -c Debug --no-restore`: 184 passed, 0 failed.
- `dotnet build UsageBeacon.sln -c Release --no-restore`: 0 warnings, 0 errors.
- The real schema-v1 cache migrated to schema v2 in under 10 seconds: all 555 legacy identities were recovered, no unpriced legacy usage or unknown model remained, and the 6.23 MB cache retained an earliest local record of 2025-11-04. The live estimate at validation time was Claude $2,134.29 and Codex $2,939.10.

Manual verification at the minimum window width and in both themes and languages remains pending.

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
- The then-active cache retention policy (D-011) dropped entries older than 180 days; D-015 now supersedes it with exact token-only compaction.
- Crash logging (D-012): verified against the built assembly by writing a real exception whose message embedded the profile path and an `sk-ant-` key; the record contained `%USERPROFILE%` and `<redacted>` and neither original value.
- CI and release automation (D-013): both workflow files parse, and the tag/version gate was dry-run locally against `UsageBeacon.csproj` (`1.0.0` matches tag `v1.0.0`).

Insights pipeline measured on 2026-07-27 against the real corpus (Claude 136 files / 276 MB, Codex 244 files / 833 MB) by invoking `DashboardViewModel` directly from the Debug build:

- Cold scan 9.50 s, warm scan 0.20 s, cache file 5.55 MB.
- The earlier "~3.1 s cold scan" figure was measured differently and is not comparable; treat 9.50 s as the current baseline for a Debug build with this corpus.
- The 5.55 MB measurement informed the later D-015 design: detailed entries stay on the 180-day window while older identities and token totals remain available.

Manual verification remains pending for: dashboard visuals in both themes and languages, refresh behavior while logs are being written, a real crash producing `%LOCALAPPDATA%\UsageBeacon\logs\crash.log` in the running application, and a live run of the release workflow against a throwaway tag.

## Local artifact cleanup

Local generated outputs were cleaned on 2026-07-19. The legacy `TokenChecker/` build tree, project and test `bin/` and `obj/` trees, and non-`latest` publish directories were removed. The only retained executable is `publish/latest/UsageBeacon.exe`. Generated outputs are recoverable by rebuilding; the removed local directories were not versioned repository content.

## Static notification-area icon validation

The dynamic usage-bar tray icon was removed on 2026-07-19 while retaining the tray menu, popup access, localized tooltip, and exit control. Validation completed with 37 passing tests, warning-free Debug and Release builds, a self-contained win-x64 single-file publish, and a successful startup from `publish/latest/UsageBeacon.exe` using the packaged static icon.
