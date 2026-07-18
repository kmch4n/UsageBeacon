$ErrorActionPreference = "SilentlyContinue"
$inputJson = [Console]::In.ReadToEnd()
$dataDirectory = Split-Path -Parent $PSCommandPath
$usagePath = Join-Path $dataDirectory "claude-native-usage.json"
$statePath = Join-Path $dataDirectory "claude-statusline-integration.json"

function Convert-RateLimitWindow($window) {
    if ($null -eq $window -or $null -eq $window.used_percentage) {
        return $null
    }

    $percentage = [Math]::Max(0.0, [Math]::Min(100.0, [double]$window.used_percentage))
    $resetsAt = [DateTime]::MinValue.ToString("O")
    if ($null -ne $window.resets_at) {
        $resetsAt = [DateTimeOffset]::FromUnixTimeSeconds(
            [long]$window.resets_at).UtcDateTime.ToString("O")
    }

    return @{
        Utilization = $percentage / 100.0
        ResetsAt = $resetsAt
    }
}

try {
    $payload = $inputJson | ConvertFrom-Json
    if ($null -ne $payload.rate_limits) {
        $entry = @{
            Usage = @{
                FiveHour = Convert-RateLimitWindow $payload.rate_limits.five_hour
                Weekly = Convert-RateLimitWindow $payload.rate_limits.seven_day
                WeeklySonnet = $null
            }
            FetchedAtUtc = [DateTime]::UtcNow.ToString("O")
            Source = 1
        }
        $json = $entry | ConvertTo-Json -Compress -Depth 6
        $temporaryPath = "$usagePath.tmp"
        [IO.File]::WriteAllText(
            $temporaryPath,
            "$json`n",
            [Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporaryPath -Destination $usagePath -Force
    }
} catch { }

$originalCommand = $null
try {
    if (Test-Path -LiteralPath $statePath) {
        $state = Get-Content -Raw -Encoding UTF8 -LiteralPath $statePath | ConvertFrom-Json
        $originalCommand = $state.OriginalCommand
    }
} catch { }

if (-not [string]::IsNullOrWhiteSpace($originalCommand)) {
    $bash = Get-Command bash -ErrorAction SilentlyContinue
    if ($null -ne $bash) {
        $inputJson | & $bash.Source -lc $originalCommand
    } else {
        $inputJson | & ([ScriptBlock]::Create($originalCommand))
    }
} elseif ($null -ne $payload.rate_limits) {
    $fiveHour = $payload.rate_limits.five_hour.used_percentage
    $sevenDay = $payload.rate_limits.seven_day.used_percentage
    if ($null -ne $fiveHour -or $null -ne $sevenDay) {
        $parts = @()
        if ($null -ne $fiveHour) { $parts += "Claude 5h: $([Math]::Round($fiveHour))%" }
        if ($null -ne $sevenDay) { $parts += "7d: $([Math]::Round($sevenDay))%" }
        Write-Output ($parts -join " | ")
    }
}
