# Claude Usage Retrieval

UsageBeacon can obtain Claude subscription usage from two sources. It prefers Claude Code's native rate-limit data and uses Anthropic's OAuth usage endpoint only as a fallback.

## Claude Code integration

Claude Code provides five-hour and seven-day rate-limit values to configured status line commands after a subscriber's first API response in a session. This path does not require UsageBeacon to make an additional usage API request.

Select the integration button next to Claude Code in the UsageBeacon popup to enable the bridge. Enabling it:

1. embeds a local PowerShell bridge under `%APPDATA%\UsageBeacon`;
2. records the existing Claude Code status line configuration locally;
3. updates `~/.claude/settings.json` to call the bridge;
4. forwards the same input to the original status line command after extracting rate-limit values.

The bridge stores only the five-hour and seven-day percentages, reset times, source, and observation time. It does not store the working directory, transcript path, session identifier, prompt content, or token values from the status line input.

Disable the integration from the UsageBeacon popup before uninstalling. The original status line configuration is restored only when the current Claude setting still points to UsageBeacon. If the setting changed after integration, UsageBeacon leaves it untouched to avoid overwriting user changes.

See Anthropic's [status line documentation](https://code.claude.com/docs/en/statusline) for the upstream `rate_limits` fields.

## OAuth fallback

When recent native rate-limit data is unavailable, UsageBeacon calls Anthropic's OAuth usage endpoint. This endpoint is not part of Anthropic's documented public API and may change or apply strict request limits.

- Automatic OAuth usage requests have a minimum interval of 30 minutes.
- A server-provided `Retry-After` value always takes precedence.
- Manual refresh bypasses the ordinary interval but never bypasses a server rate-limit cooldown.
- Cached usage is written only after a successful response.
- The UI identifies the data source and the last successful observation time.

UsageBeacon refreshes an expired OAuth access token by using the refresh token and the same OAuth request format as the installed Claude Code client. A successful refresh can rotate the refresh token, so UsageBeacon writes the refreshed OAuth fields back only when the credential came from a supported local Windows credential file.

The update follows these safety rules:

- only the access token, refresh token, expiry, and scopes are changed;
- unrelated root and OAuth fields are preserved;
- the original OAuth state must still match before replacement, so a concurrent sign-in or refresh is not overwritten;
- a same-directory temporary file, inherited access rules, a backup, and atomic file replacement protect the original file;
- credentials are never written to UsageBeacon's usage cache or logs;
- a refreshed credential remains available in memory if persistence temporarily fails, and persistence is retried during the same process lifetime.

UsageBeacon does not refresh credentials read from Windows Credential Manager or WSL because it cannot safely persist a rotated refresh token to those sources yet. If refresh cannot be completed safely, sign in again with `claude auth login`. A concurrent non-cooperating process can still change the credential file between the final comparison and replacement; UsageBeacon treats persistence as best-effort and never falls back to an ordinary overwrite.

## Troubleshooting

If Claude usage remains stale:

1. Run `claude auth status --json` and confirm that Claude reports a Claude.ai login.
2. If native integration is enabled, send one Claude Code request so the status line receives `rate_limits` data.
3. Check the source and observation time shown below the Claude usage bars.
4. Wait until the displayed retry time if the OAuth endpoint returned HTTP 429.
5. Run `claude auth login` if UsageBeacon reports expired authentication.

Do not include credential files, access tokens, refresh tokens, or unredacted status line input in bug reports.
