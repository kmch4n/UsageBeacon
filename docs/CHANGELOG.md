# Changelog

All notable user-visible changes to UsageBeacon will be documented in this file.

## Unreleased

### Added

- Automatic migration of the legacy Token Checker settings directory and startup registration.
- English project documentation for contributing, security reporting, and attribution.
- Optional Claude Code status line integration for native five-hour and weekly usage data.
- Automated tests for Claude OAuth refresh, usage responses, rate-limit cooldowns, credential expiry, and status line preservation.
- Runtime-selectable English and Japanese interfaces with system-language detection and English fallback.

### Changed

- Renamed the application, solution, projects, executable, and namespaces to UsageBeacon.
- Reframed the repository as an independent, unofficial community fork with explicit upstream credit.
- Reduced automatic Claude OAuth usage polling to a minimum interval of 30 minutes.
- Preserved server-provided rate-limit cooldowns during manual refresh.
- Recorded Claude cache freshness and data source only after successful retrieval.
- Moved user-facing text into extensible .NET localization resources and standardized production source comments in English.
- Replaced language-dependent taskbar clock detection with geometry-based detection.
- Persisted rotated OAuth credentials safely so authentication survives application and Windows restarts.
- Prevented refresh attempts for credential sources that cannot safely store rotated tokens.

### Fixed

- Renewed an unpersisted rotated credential from its own refresh token instead of reusing the stale on-disk token.
- Kept the background polling loop alive when a UI subscriber fails.
- Fetched Claude usage immediately after a restart when a cooldown is active but no cached usage exists.
- Forwarded a preserved status line command through cmd.exe so its quoting and syntax survive on machines with other shells installed.
- Reduced idle CPU usage by caching taskbar UI Automation measurements and rescanning only on geometry changes.
- Showed "no recent usage" instead of "Reset soon" when Codex reports no reset time.

## Upstream history

Changes made before the UsageBeacon rename belong to the history of [satonico/Token-Checker-win](https://github.com/satonico/Token-Checker-win) and this repository's Git history.
