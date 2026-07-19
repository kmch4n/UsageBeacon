# Project Memory

Last reviewed: 2026-07-19

## Identity

- The product name is **UsageBeacon**.
- UsageBeacon is an independent, unofficial community fork of `satonico/Token-Checker-win`.
- The project is not affiliated with or endorsed by the upstream maintainer, Anthropic, or OpenAI.
- Upstream attribution and the original MIT License must remain intact.

Evidence: [`README.md`](../README.md), [`docs/NOTICE.md`](../docs/NOTICE.md), and [`LICENSE`](../LICENSE).

## Technology and structure

- `UsageBeacon.sln` contains a .NET 8 WPF application and xUnit tests.
- Application code lives in `UsageBeacon/`; tests live in `UsageBeacon.Tests/`.
- Public documentation other than `README.md` belongs under `docs/`.
- GitHub Issue Forms and pull request templates belong under `.github/`.
- API and Windows integration code must remain outside views and follow the existing `Providers/`, `Services/`, and `Utilities/` boundaries.
- Claude usage retrieval prefers native Claude Code status line data and uses the OAuth usage endpoint as a rate-limited fallback.

## Language conventions

- Repository-facing documentation, code comments, commit messages, Issues, and pull requests are written in English.
- The application UI supports English and Japanese at runtime, follows the supported Windows UI language by default, and falls back to English.
- User-facing text belongs in .NET resource files. Adding a language requires a translated resource file and one language catalog entry.

## Security boundaries

- Never commit Claude or Codex credentials, tokens, local caches, or machine-specific paths.
- Credential discovery, WSL integration, startup registration, local cache handling, command execution, and network clients are security-sensitive.
- OAuth refresh may update only supported local Windows credential files. Updates must preserve unknown JSON fields and file access rules, compare the previously read OAuth state before replacement, and never copy credentials into UsageBeacon caches or logs.
- Credential Manager and WSL credential sources are read-only until source-specific persistence is implemented; rotating a refresh token without being able to save it is prohibited.
- Claude status line integration is opt-in, must preserve an existing command, and must persist only rate-limit values and observation metadata.
- Public bug reports must direct vulnerability reports to the repository security policy.

Evidence: [`docs/SECURITY.md`](../docs/SECURITY.md) and [`.github/ISSUE_TEMPLATE/config.yml`](../.github/ISSUE_TEMPLATE/config.yml).
