# Repository Guidelines

## Project Structure & Module Organization

`UsageBeacon.sln` contains a .NET 8 WPF application under `UsageBeacon/` and xUnit tests under `UsageBeacon.Tests/`. Startup code is in `App.xaml` and `App.xaml.cs`.

- `Views/`: windows, widget UI, and code-behind
- `Controls/`: reusable WPF controls
- `ViewModels/`: UI state and presentation logic
- `Providers/`: Claude and Codex usage retrieval
- `Services/`: API clients and Windows integration
- `Models/`: usage data and domain errors
- `Utilities/`: focused platform and window helpers
- `Resources/`: icons and static application assets

Keep new code within these existing boundaries. Avoid placing API calls or Windows integration directly in views.

## Build, Run, and Publish

Run commands from the repository root in PowerShell:

```powershell
dotnet restore UsageBeacon.sln
dotnet build UsageBeacon.sln -c Debug
dotnet test UsageBeacon.sln -c Debug
dotnet run --project UsageBeacon\UsageBeacon.csproj
dotnet build UsageBeacon.sln -c Release
```

Create the distributable executable with:

```powershell
dotnet publish UsageBeacon\UsageBeacon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\
```

Do not track outputs from `bin/`, `obj/`, or `publish/`.

## Project Conventions

Follow the global agent rules for formatting, naming, encoding, Git operations, and commit messages. Preserve nullable reference types, use the `Async` suffix for asynchronous methods, and follow the existing XAML layout. Repository documentation and source comments are written in English. User-facing text must use the localization resources; English and Japanese are currently supported with English as the neutral fallback language.

## Repository Memory

Repository-specific knowledge must be explicit and versioned. Do not rely on unstated assumptions, prior conversations, or agent-only context.

- Read `.memory/README.md` and the memory files it indexes before planning or changing the repository.
- Verify drift-prone entries against the current source, Git state, or external system before relying on them.
- Record every non-obvious constraint, decision, compatibility contract, or pending state that affects the work in `.memory/` as part of the same change.
- Update or supersede existing entries instead of adding contradictory duplicates.
- Keep shared memory in English and include dates, status, and source evidence where applicable.
- Never store credentials, tokens, personal data, or absolute machine-specific paths in shared memory.
- Put unavoidable local-only notes under `.memory/local/`. That directory is ignored and must never contain knowledge required to maintain the repository.
- Before handoff, make sure `.memory/` agrees with the implementation and remove or mark stale statements.

## Testing Guidelines

Automated tests live in `UsageBeacon.Tests/`. Before submitting code changes, run tests and build both Debug and Release configurations. Manually verify widget placement, popup behavior, refresh intervals, startup registration, and Claude/Codex states with missing, valid, and expired credentials when relevant. UI changes require screenshots in the pull request. Use behavior-based test names such as `FetchAsync_ReturnsCachedUsage_WhenAuthenticationExpires`.

## Security & Configuration

Never commit Claude or Codex credentials, tokens, local caches, or machine-specific paths. Treat credential discovery, WSL access, startup registration, and network-client changes as security-sensitive. Document expected fallback and failure behavior in the pull request.
