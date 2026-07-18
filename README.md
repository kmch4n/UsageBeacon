# UsageBeacon

UsageBeacon is a lightweight Windows app that keeps Claude Code and Codex usage visible on the taskbar. Click the widget to see usage windows, reset times, refresh controls, and local settings.

> [!IMPORTANT]
> UsageBeacon is an independent, unofficial community fork of [satonico/Token-Checker-win](https://github.com/satonico/Token-Checker-win). It is not affiliated with or endorsed by the upstream maintainer, Anthropic, or OpenAI. The fork exists to continue Windows-focused maintenance and development while preserving clear credit for the original work.

## Features

- Taskbar widget for Claude Code and Codex usage
- Detailed five-hour and weekly usage windows
- Reset-time countdowns and manual refresh
- Windows and WSL Claude credential discovery
- Codex CLI discovery, including nvm-windows installations
- Multi-monitor and virtual desktop support
- Optional startup registration and configurable polling
- Local caching that keeps the last successful values visible during transient failures

Either Claude Code or Codex can be used independently; both are not required.

## Requirements

- Windows 10 or Windows 11, 64-bit
- Claude Code CLI with `claude auth login`, if Claude usage is needed
- Codex CLI with `codex login`, if Codex usage is needed

The prebuilt self-contained executable does not require a separate .NET installation. Building from source requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## Install

### Download a release

Download `UsageBeacon.exe` from the [Releases page](https://github.com/kmch4n/UsageBeacon/releases) and run it.

Windows SmartScreen may warn about an unsigned executable. Review the release source and checks before choosing **More info** and **Run anyway**. Building from source is available for users who prefer not to run an unsigned download.

### Build from source

```powershell
git clone https://github.com/kmch4n/UsageBeacon.git
cd UsageBeacon
dotnet build UsageBeacon.sln -c Release
```

The application is written to:

```text
UsageBeacon\bin\Release\net8.0-windows\UsageBeacon.exe
```

Create a self-contained, single-file build with:

```powershell
dotnet publish UsageBeacon\UsageBeacon.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\
```

## Usage

1. Sign in to the CLIs you want to monitor:

   ```powershell
   claude auth login
   codex login
   ```

2. Start `UsageBeacon.exe`.
3. Click the taskbar widget to open the detailed usage popup.
4. Use the tray menu for refresh, monitor switching, and exit controls.

### Claude Code in WSL

If Claude Code is installed only inside WSL, open the login window in UsageBeacon and select **WSL**. The app launches `claude auth login` in an interactive WSL shell and then discovers the credential file from the WSL filesystem.

## Data and privacy

- Claude credentials are read locally from Windows Credential Manager, known Claude credential files, or WSL. The access token is sent only to Anthropic's usage endpoint.
- Codex usage is read through the locally installed `codex app-server`; UsageBeacon does not parse or store the Codex access token.
- Settings and usage caches are stored under `%APPDATA%\UsageBeacon`.
- UsageBeacon does not include telemetry or analytics.

When upgrading from Token Checker for Windows, UsageBeacon attempts to migrate `%APPDATA%\TokenChecker` and the legacy Windows startup entry automatically. If migration is blocked, it continues using the existing data directory rather than discarding settings.

## Uninstall

Exit UsageBeacon, then remove its startup entries and local data if desired:

```powershell
Stop-Process -Name UsageBeacon -Force -ErrorAction SilentlyContinue
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v UsageBeacon /f 2>$null
reg delete "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v TokenChecker /f 2>$null
Remove-Item "$env:APPDATA\UsageBeacon" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:APPDATA\TokenChecker" -Recurse -Force -ErrorAction SilentlyContinue
```

Delete the downloaded executable or cloned repository afterward. Claude and Codex credentials are managed by their respective CLIs and are not removed by these steps.

## Development

Contributions are welcome. Run Debug tests and both Debug and Release builds before submitting changes. Report credential-related vulnerabilities privately rather than through a public issue.

## Attribution

UsageBeacon is based on [Token Checker for Windows](https://github.com/satonico/Token-Checker-win) by satonico224, which in turn ports the macOS [Token Checker](https://github.com/satonico/Token-Checker) experience to Windows. The original copyright notice and MIT License are preserved in [LICENSE](LICENSE).

The UsageBeacon maintainers are grateful for the upstream design and implementation. References to the upstream projects are for attribution and history only and do not imply endorsement of this fork.

## License

Licensed under the [MIT License](LICENSE).

## Disclaimer

UsageBeacon is provided **as is**, without warranty. Usage data may be delayed, incomplete, or affected by changes to third-party CLIs and APIs. You are responsible for reviewing the software and protecting your credentials before use.
