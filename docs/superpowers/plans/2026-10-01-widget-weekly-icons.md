# Widget Weekly Usage and Official Icons Implementation Plan

> **For agentic workers:** Execute this plan in the current session with `superpowers:executing-plans`. Use focused red-green tests for behavior changes. Preserve unrelated working-tree edits.

**Goal:** Add opt-in, same-size weekly percentages beside five-hour percentages and replace widget text symbols with packaged official Claude and OpenAI images.

**Architecture:** `AppSettings` and `UsageViewModel` own the persisted boolean. The popup exposes it; `TaskbarWidget` observes it and selects either the existing widths or a wider one-line layout. Existing taskbar geometry and notification clearance remain the only placement policy. The two image files become WPF resources and are never fetched at runtime.

**Tech Stack:** .NET 8, WPF, Win32 taskbar placement, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-01-widget-weekly-icons-design.md`.

## Global constraints

- Keep the existing independent, unofficial product attribution and do not imply Anthropic or OpenAI endorsement.
- Keep every percentage number at 14 DIP in the wide layouts; no visible period labels.
- The setting defaults to false, persists in the existing JSON settings, and rolls back after save failure.
- Preserve the existing 26 physical px notification-area clearance and native-window recheck.
- Write code, repository documentation, comments, and resource keys in English; provide English and Japanese UI strings.
- Keep all existing unrelated uncommitted changes, including the app icon, dashboard, README, and previous widget fix.

## Review focus

1. An old settings file without the new key must leave the weekly option off.
2. A failed setting save must return the checkbox and widget to their accepted state.
3. A missing five-hour value must not be filled with the weekly value when four values are shown.
4. The wider layout must use the existing out-of-taskbar fallback instead of covering taskbar buttons or the notification area.
5. All display modes must load both image resources; keyboard activation and focus feedback must remain intact.

## Task 1: Persist and expose the option

**Files:** `UsageBeacon/Models/AppSettings.cs`, `UsageBeacon/ViewModels/UsageViewModel.cs`, `UsageBeacon/Views/UsagePopupWindow.xaml`, `UsageBeacon/Views/UsagePopupWindow.xaml.cs`, both `UsageBeacon/Resources/Strings*.resx`, `UsageBeacon.Tests/AppSettingsStoreTests.cs`, `UsageBeacon.Tests/UsageViewModelTests.cs`, `UsageBeacon.Tests/UsagePopupWindowTests.cs`.

- [x] Add a failing settings test: a legacy JSON document loads `ShowWeeklyInWidget == false`; saving true and reloading yields true.
- [x] Add a failing view-model test: setting true persists; a thrown save restores false and reports `SettingsSaveFailed`.
- [x] Add a failing popup test: toggling the new checkbox updates the view model and a save failure restores its visual state.
- [x] Add `bool ShowWeeklyInWidget` to `AppSettings`; load, expose, save, and notify from `UsageViewModel` following the existing setting pattern.
- [x] Add one localized checkbox row to the popup settings, with synchronization guards matching `StartupChk`.
- [x] Run the focused settings, view-model, popup, and localization tests until green.

## Task 2: Render four values without changing their font size

**Files:** `UsageBeacon/Views/TaskbarWidget.xaml`, `UsageBeacon/Views/TaskbarWidget.xaml.cs`, `UsageBeacon.Tests/TaskbarWidgetTests.cs`.

- [x] Add failing widget tests for off/on layout width and visibility, four independent values including missing data, and a widened right placement with the existing notification clearance.
- [x] Add two weekly percentage text blocks next to the existing five-hour values, keeping all wide-layout numbers at 14 DIP and both services in horizontal groups.
- [x] Size the regular wide layout to fit the official icons and three-digit percentages, and select a larger extended width only when the option is on. If that width cannot fit inside a verified taskbar corridor, retain all four values outside the taskbar. Recompute layout immediately when the saved option changes.
- [x] Give the unlabeled values an explicit localized tooltip and accessible name; keep button Invoke, focus, and click behavior.
- [x] Run focused widget tests until green.

## Task 3: Package the official image assets

**Files:** `UsageBeacon/Resources/claude.png`, `UsageBeacon/Resources/openai.png`, `UsageBeacon/UsageBeacon.csproj`, `UsageBeacon/Views/TaskbarWidget.xaml`, `UsageBeacon.Tests/TaskbarWidgetTests.cs`, `docs/NOTICE.md`.

- [x] Verify the downloaded official files against the spec's dimensions and SHA-256 digests; copy them unchanged to `Resources/`.
- [x] Add a failing XAML/widget test that expects the resource-backed images in wide, compact, and vertical modes and no legacy symbol text.
- [x] Register both images as WPF resources and use them at a fixed small icon size with high-quality bitmap scaling and preserved aspect ratio.
- [x] Record the source URLs and ownership in `docs/NOTICE.md`, then run focused tests; inspect the source images. Live widget clarity remains unverified.

## Task 4: Validate and publish the local build

**Files:** `.memory/STATUS.md`, `docs/CHANGELOG.md`, `publish/latest/UsageBeacon.exe` (ignored build output).

- [x] Run Debug and Release full test suites and `-warnaserror` solution builds; run `git diff --check` and inspect the task-related diff.
- [ ] Verify both images and all four text values at real widget size, including a 150% scaling scenario if an isolated visual check is possible without disturbing the running app.
- [x] Publish the verified working tree to `publish/latest/UsageBeacon.exe` using the documented self-contained single-file command, then record its hash and timestamp. Do not create another retained publish destination.
- [x] Update changelog and repository status with the actual verification and remaining live-UI limits. Keep the existing running process untouched.

## Execution note

Debug and Release each passed 290 tests; both builds passed with zero warnings under -warnaserror. The independent review identified unknown left-side boundaries and an unlabeled checkbox; both were corrected. The local build was published to publish/latest/UsageBeacon.exe. The running Debug application was not restarted, so real-size and mixed-DPI widget rendering remain unchecked. Taskbar button boundary data can remain cached for up to five seconds; a transient button overlap remains possible while the notification-area clearance is still checked every 200 ms.
