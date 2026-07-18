# Contributing to UsageBeacon

Thank you for helping improve UsageBeacon. This project is an independent, unofficial fork of [satonico/Token-Checker-win](https://github.com/satonico/Token-Checker-win); contributions should preserve upstream attribution and must not imply upstream endorsement.

## Before you start

- Search existing issues and pull requests before opening duplicate work.
- Discuss broad behavioral, security-sensitive, or visual changes before implementation.
- Never include Claude or Codex credentials, tokens, local caches, or machine-specific paths.

## Development setup

Install the .NET 8 SDK, then run these commands from the repository root:

```powershell
dotnet restore UsageBeacon.sln
dotnet build UsageBeacon.sln -c Debug
dotnet test UsageBeacon.sln -c Debug
dotnet build UsageBeacon.sln -c Release
```

Run the application with:

```powershell
dotnet run --project UsageBeacon\UsageBeacon.csproj
```

## Code conventions

- Use four spaces for indentation and keep nullable reference types enabled.
- Use double quotes and add type annotations where applicable.
- Give asynchronous methods the `Async` suffix.
- Keep API access and Windows integration out of views.
- Follow the existing XAML structure for targeted UI changes.
- Write code, comments, commit messages, and repository documentation in English.
- The current application UI is Japanese; keep existing UI language consistent unless a change explicitly introduces localization.

## Testing

Add behavior-based xUnit tests under `UsageBeacon.Tests`. For platform or UI changes, also verify taskbar placement, popup behavior, refresh intervals, startup registration, and missing, valid, and expired credential states as applicable.

Claude retrieval changes should cover OAuth success and refresh, HTTP 401 and 429 responses, `Retry-After` handling, cache freshness, native status line input, and preservation of an existing Claude Code status line command. Tests and reports must use synthetic credentials and paths.

User-facing text must use the localization resources rather than C# or XAML literals. Keep neutral English and every translation resource in sync, preserve format placeholders, and verify runtime switching and layout with long English strings. See [Localization](LOCALIZATION.md) for the translation workflow.

## Pull requests

Keep each pull request focused. Describe the user-visible effect, fallback behavior, security impact, and verification performed. Include screenshots for visible UI changes.

By contributing, you agree that your contribution is provided under the repository's MIT License.
