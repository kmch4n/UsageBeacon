# Security Policy

## Supported versions

Security fixes are applied to the latest UsageBeacon release and the current `main` branch. Older releases may not receive backports.

## Reporting a vulnerability

Do not open a public issue for vulnerabilities involving credentials, token exposure, command execution, unsafe path handling, or privilege boundaries.

Use GitHub private vulnerability reporting for this repository when available. If it is unavailable, contact the maintainer through a private channel listed on the maintainer's GitHub profile before public disclosure.

Include the affected version, reproduction steps, expected impact, and any suggested mitigation. Do not include real Claude or Codex credentials in the report.

The optional Claude Code integration changes the local `~/.claude/settings.json` status line command after explicit user confirmation. It preserves the previous status line configuration locally and forwards input to that command. Reports about this integration must redact status line input because it can contain local paths and session metadata, even though UsageBeacon persists only rate-limit values and timestamps.

OAuth refresh can update Claude Code's local Windows credential file because a successful refresh may rotate the refresh token. UsageBeacon updates only known OAuth fields, preserves unknown fields and file access rules, verifies that the previously read OAuth state has not changed, and uses replacement with a temporary backup. It does not copy credentials into its own cache or logs. Credential Manager and WSL sources remain read-only until source-specific safe writers exist.

UsageBeacon is an independent, unofficial fork. Reports that affect the upstream project should also be coordinated responsibly with the upstream maintainer when appropriate.
