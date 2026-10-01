# Widget Weekly Usage and Official Icons Design

## Intent

Keep Claude Code and Codex as two horizontal groups in the taskbar widget. Let users opt in to seeing both the five-hour and weekly utilization in each group, with all four percentages at the current primary percentage font size. The left percentage in each group always represents five hours and the right percentage always represents the week. The widget does not print period labels; the tooltip and detail popup explain the order.

## Behavior

- Add a persisted "Show weekly usage in widget" checkbox to the popup settings, off by default for existing installations. A successful change updates the widget immediately; a failed save restores the accepted state.
- When off, retain the existing compact, vertical, and wide layout behavior. When on, show the two percentages side by side for each service at the same 14 DIP font size, expand the horizontal widget width, and retain a single row. Do not shrink text or silently hide the weekly value when space is insufficient. Use the existing outside-taskbar fallback and actual HWND clearance check when no safe inline slot exists.
- In the opt-in layout, an unavailable five-hour or weekly value is shown as `--%` in its own position. Do not reuse a weekly value as a five-hour value. Keep the popup's explicit labels and reset information.
- Replace the existing `✦` and `▶` text stand-ins in all widget modes with locally packaged, unmodified icons from Claude's official site and OpenAI's official developer site. The user accepted the OpenAI logo for Codex. Do not use a third-party icon pack or claim an official affiliation. Preserve the clickable WPF button, focus feedback, and accessible name.
- The accessible description and tooltip identify the service, five-hour and weekly values when the option is on, while still explaining that activation opens the usage details. Missing data is announced as unavailable. English and Japanese strings stay in resources.

## Asset provenance

- Claude site icon: `https://assets.claude.com/95a868946ac8a31e5ff832e2899f294aa368b836.png?w=32&h=32`, observed at 32x32 pixels, SHA-256 `4004381309500f25e49a1ae639912ce307ea81da74cc383954a1ac8b9d343fe6`.
- OpenAI Developers favicon: `https://developers.openai.com/favicon.png`, observed at 48x48 pixels, SHA-256 `8d5575ee667ff715cd3e3074d5296edc68d5aadd89720d203e13131b68b22a04`.
- These marks identify monitored services inside an independent app. Record the sources and ownership in `docs/NOTICE.md`; retain its no-endorsement statement. OpenAI's [design guidelines](https://openai.com/brand/) require using provided marks without alteration and avoiding endorsement confusion.

## Acceptance

- Off preserves the old amount and order of visible information; on shows four distinct values on one line, including independent placeholders.
- Both setting changes survive restart, and a save failure leaves the prior layout active.
- The 26 physical px notification clearance, out-of-taskbar fallback, monitor selection, virtual desktop behavior, and button accessibility remain intact.
- Both icons render clearly on the dark taskbar at ordinary and 150% scaling; no network request is needed at runtime.
- Debug and Release tests/builds pass, and the current working tree is published to the canonical `publish/latest/UsageBeacon.exe` after verification. The running instance is not interrupted for this work.
