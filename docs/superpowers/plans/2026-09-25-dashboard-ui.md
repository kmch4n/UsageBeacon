# Dashboard UI implementation plan

## Goal

Make the WPF dashboard feel integrated with its theme, make estimated costs readable in USD, JPY, and EUR, and make daily and model usage easier to inspect. Keep all data local to this PC.

## Architecture

Keep USD as the sole stored cost unit. Add a small presentation formatter with fixed, explicitly approximate display conversions. Add day-level token totals to the existing 30-day aggregation. Render chart selection and 7/30-day controls in `DashboardWindow`; persist the chosen currency in the existing application settings through `UsageViewModel`. Use WPF `WindowChrome` for an integrated title bar while preserving resize, drag, maximize, and system commands.

## Tech stack

.NET 8, WPF, xUnit. No new UI or network dependency.

## Approved design

The user selected `frontend-design`. Retain the existing dark/light palette and service colors. Put the currency control in the upper toolbar. Give the chart more space, add a 7/30-day switch and a selected-day summary, and improve the model table's hierarchy. Clearly label estimates and local coverage. Do not add a remote-device collector or cloud-history integration.

## Implementation steps

1. Add tests for USD/JPY/EUR conversion, formatting, and invalid saved preferences. Run the focused tests and observe failure; implement the formatter and settings persistence; rerun.
2. Add tests for day-level token counts, unknown-price indication, and empty days. Run failing tests; extend the aggregator and daily model; rerun.
3. Replace the standard title bar with a theme-aware WPF chrome and accessible window controls. Add currency and range selectors; chart bars become keyboard-accessible day buttons with a selected-day summary. Improve spacing and model-table readability.
4. Update localization and dashboard documentation. Build Debug and Release, run the full test suite and `git diff --check`. Inspect the dashboard visually if the desktop runtime permits; report any visual behavior that remains unverified.

## Review focus

Preserve unrelated working-tree edits. Check that currency changes never alter cached USD values, settings updates preserve currency, chart selection survives range changes, and window controls remain usable. Do not stage, commit, push, or deploy.
