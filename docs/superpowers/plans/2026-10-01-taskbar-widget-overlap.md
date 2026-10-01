# Taskbar Widget Overlap Implementation Plan

**Goal:** Prevent the observed sustained widget overlap during ordinary notification-area width changes, and promptly retreat from any newly detected collision. A floating window cannot guarantee zero overlap between external geometry changes and observation.

**Architecture:** Keep taskbar discovery in `TaskbarPosition` and WPF placement in `TaskbarWidget`. Add a cheap rectangle read that never enters UI Automation, and compare the actual widget and `TrayNotifyWnd` rectangles from the same Win32 caller. Reserve at least one observed tray-icon step (24 physical px) beyond the ordinary 2 px gap, check geometry every 200 ms before topmost reassertion, and place the widget outside the taskbar toward the screen interior whenever the notification area, centered taskbar buttons, or invalid measurements leave no verified inline slot. Preserve the current wide, compact, vertical, and out-of-taskbar modes, keyboard access, and popup behavior.

**Tech Stack:** .NET 8, WPF, Win32 taskbar/window APIs, xUnit.

**Evidence:** On 2026-10-01, a read-only 100 ms probe observed the widget right edge at 2179 while `TrayNotifyWnd` began at 2157 for six consecutive samples, a 22 px intersection. The normal measured gap was 2 px. `TaskbarWidget` checks placement every fifth 200 ms timer tick and reasserts topmost every tick. These readings establish a transient overlap but do not establish that pointer movement caused the notification-area resize.

## Global constraints

- Treat the user's existing uncommitted icon, dashboard, README, changelog, and status changes as unrelated; do not stage or rewrite them.
- Keep the widget a real WPF `Button` with keyboard activation, focus indication, and UI Automation Invoke support (`.memory/DECISIONS.md`, D-014).
- Do not launch a second UsageBeacon instance or interrupt the running instance without a reason tied to live verification.
- Never call `TaskbarPosition.Get()` from the 200 ms collision check: its UI Automation cache deliberately rescans after a notification rectangle change.
- Compare Win32 widget and notification rectangles in one process DPI context. Convert any chosen movement to WPF units using the widget's `CompositionTarget` transform, then verify the actual HWND rectangle again.
- Keep Windows integration in `Utilities` or focused services, not in the view's event handlers.
- Use UTF-8 without BOM and LF for changed Markdown; write repository documentation and comments in English.

## Files

- Modify `UsageBeacon/Utilities/TaskbarPosition.cs`: expose cheap current taskbar/notification rectangles independently of `Get()` and a pure physical-rectangle safety decision. Keep the UI Automation cache for slower content measurement.
- Modify `UsageBeacon/Views/TaskbarWidget.xaml.cs`: check current geometry before topmost reassertion and on taskbar changes; avoid placing the widget over notification-area bounds.
- Modify `UsageBeacon.Tests/TaskbarPositionTests.cs` and `UsageBeacon.Tests/TaskbarWidgetTests.cs`: deterministic changing-rectangle and layout-mode regression tests.
- Update `docs/CHANGELOG.md` and `.memory/STATUS.md` only with the verified outcome. Preserve pre-existing edits in both files.

## Task 1: Pin the geometry contract with failing tests

- [ ] Add a pure rectangle decision test for an inline widget that starts with a 2 px gap and then encounters a 24 px leftward notification-area expansion. It must report an unsafe inline position before the widget is made topmost again and return a safe corridor or above-taskbar fallback.
- [ ] Add coverage for a stable position with at least a 26 physical px clearance, no notification window, a left-side placement, and the narrow-slot fallback. Use explicit Win32 rectangles and assert that any accepted inline widget rectangle has no intersection with the notification area or the measured centered-content boundary. Verify that fallback lies below a top taskbar and above a bottom taskbar, inside the selected screen.
- [ ] Add a widget-level test that exercises the placement decision with synthetic taskbar geometry, including a movement between scheduled full UI Automation scans. Confirm that the chosen mode/position remains clickable and preserves the accessible button.
- [ ] Add a sequence test for initial placement and for `safe quick check -> full layout tick`: both must end with at least 26 physical px clearance or above-taskbar placement. No placement path may restore the old 2 px anchor.
- [ ] Run the focused xUnit tests and confirm that the new overlap case fails for the intended reason before modifying production code.

## Task 2: Make live positioning collision-aware

- [ ] Add a cheap Win32 read of the current taskbar and `TrayNotifyWnd` rectangles that does not call `GetUiaMeasurements`. Read the widget HWND rectangle through `GetWindowRect` in the same caller. If either taskbar or notification bounds are missing, invalid, or auto-hidden, do not accept a right-side inline position; retreat just outside the taskbar toward the selected screen interior.
- [ ] Route initial `SnapToTaskbar`, display-change/monitor/placement actions, the one-second full layout, and the 200 ms quick check through one collision decision. The full layout must reserve the same 26 physical px at the notification edge; convert the reserve to the widget's WPF logical units before computing available width and `Left`. No path may restore the old 2 px anchor.
- [ ] Move only this cheap collision check to each existing 200 ms timer tick and run it before `ShowWindow`/`SetWindowPos(HWND_TOPMOST)`. Keep `TaskbarPosition.Get()` and its 5-second UI Automation cache on the slower full layout pass.
- [ ] When a right-side inline position intersects the current notification area or leaves less than 26 physical px clearance, retreat outside the taskbar instead of shifting left across an unverified centered-content area. The slower full layout may return inline after the notification rectangle has stayed stable for one second, but only when it has a measured centered-content edge. If UI Automation yields no buttons, remain outside the taskbar because no safe inline corridor can be established. A read-only probe on the current shell returned zero taskbar UI Automation buttons, so this may move the widget outside the taskbar until a reliable boundary source is available.
- [ ] Convert any physical shift to WPF logical movement using the active widget's presentation transform, then re-read its HWND rectangle. If the resulting position still intersects or cannot be measured, hide the native widget until the next successful geometry check rather than reassert topmost over the icons. For a top taskbar, place the widget below its bottom edge; for a bottom taskbar, place it above its top edge. Keep a conservative rightward-movement delay so a short notification-area contraction cannot immediately put the widget back into its expansion path.
- [ ] Recalculate on display changes and monitor/placement selection. Ensure closing the window stops timers and any new event subscriptions.
- [ ] Run focused tests to green. Inspect the diff for unintended effects on compact/vertical display and virtual-desktop visibility.

## Task 3: Verify the real behavior and record limits

- [ ] Run `dotnet test UsageBeacon.sln -c Debug --no-restore --nologo`, then Debug and Release builds with `-warnaserror`, using a separate output directory if the running process locks the default output.
- [ ] Run `git diff --check` and review only task-related diffs. Confirm the pre-existing dirty files remain intact.
- [ ] If a safe isolated launch is possible without changing the user's active settings or replacing the running executable, measure the **new build's** widget and notification rectangles at 100 ms intervals while the notification area changes width. Record any negative gap and the duration. The old running binary cannot validate this change; otherwise report live validation as pending.
- [ ] Update `.memory/STATUS.md` and `docs/CHANGELOG.md` with test results, live-observation status, and the limit that an external taskbar may move between observations.
- [ ] Request an independent review of the final diff and correct confirmed issues before handoff.

## Review focus

1. A 24 px notification-area expansion while the widget is at the old right anchor must either be covered by the 26 px reserve or trigger retreat at the next 200 ms check, not wait for the slower full reposition.
2. The notification area disappearing, returning, or reporting an invalid rectangle must place the widget outside the taskbar and inside the selected screen rather than assume an inline right edge.
3. Mixed-DPI monitors must compare Win32 rectangles with each other and verify the moved HWND rectangle after a WPF coordinate conversion.
4. A measured narrow inline gap must select compact, vertical, or out-of-taskbar mode. With no UI Automation content edge, remain out of the taskbar instead of guessing a corridor.
5. Rapid width oscillation must not make the widget visibly jump into the notification area or break its button behavior.

## Implementation outcome (2026-10-01)

- The independent plan audit identified the need for one shared clearance policy, an actual HWND recheck after a WPF move, and an inside-screen fallback for a top taskbar. All were implemented. Final-diff review then found and verified fixes for missing `ContentRight`, missing notification bounds, and screen-edge clamping.
- Focused regression tests first failed against the old placement. The final Debug and Release suites each passed 281 tests. Both solution builds passed with `-warnaserror`, zero warnings, and zero errors.
- The running instance still uses the old binary. Live pointer and notification-area behavior, especially across mixed-DPI monitors, requires validation after the new build is launched. The 200 ms sampling interval can permit a brief overlap between samples.
