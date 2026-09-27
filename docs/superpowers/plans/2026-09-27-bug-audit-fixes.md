# Pre-release Bug Audit Fixes

**Goal:** Correct newly identified reliability, privacy, and state-reporting bugs before the next release.

**Scope:** Existing behaviors only. Feature Issues remain deferred, and no release is authorized by this plan.

## Audit and implementation order

1. **Crash dialogs:** Reproduce disclosure with synthetic exception text. Replace raw exception messages in startup, unhandled-error, and integration-failure dialogs with localized safe text. Keep redacted local diagnostics.
2. **Live transcript sharing:** Reproduce a read while a synthetic writer holds a Claude transcript open. Use compatible file sharing and retain per-file scan isolation.
3. **Codex handshake recovery:** Reproduce an initialize timeout with a synthetic process that remains alive. On failure, stop that generation and allow a fresh handshake; review synchronous pipe-write failure and pending-request cleanup.
4. **Credential switching:** Reproduce valid account A changing to valid account B between fetches. Honor the current credential source while preserving the pending rotated-token safeguards and safe fallback behavior.
5. **Cancellation:** Reproduce caller cancellation during provider fetch. Preserve cancellation instead of publishing a network error or changing polling cooldown state.
6. **Startup registration:** Reproduce a Run entry that points to a moved executable. Report enabled only when registration can launch the current executable, while preserving legacy migration behavior.
7. **Independent review follow-up:** Preserve a pending rotated Claude credential after a temporary source-read failure, and repair a stale current Run entry when migrating a legacy registration. Reproduce both regressions before the correction.
8. **Popup error privacy:** Check whether untrusted network, RPC, or decoding details reach localized usage error text. Replace those details with safe localized messages after synthetic secret regressions fail.

## Verification

- Observe each regression test fail for the reported behavior before changing production code, then pass after the minimal fix.
- Run focused suites, complete Debug and Release tests/builds, and `git diff --check`.
- Obtain independent review of security-sensitive and cross-component changes. Record remaining manual-validation limits in `.memory/STATUS.md`.
- Leave GitHub Issues, commits, tags, pushes, and releases untouched unless separately authorized.
