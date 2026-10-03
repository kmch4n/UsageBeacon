using UsageBeacon.Models;
using UsageBeacon.Utilities;

namespace UsageBeacon.Localization;

public static class LocalizedText
{
    // Unexpected exceptions can contain credentials or local paths. Keep their
    // details in the redacted crash log instead of placing them in a dialog.
    public static string UnexpectedError(Exception _, string resourceKey)
        => LocalizationService.Get(resourceKey);

    public static string DomainError(DomainError error) => error.Kind switch
    {
        DomainErrorKind.TokenMissing => LocalizationService.Get("ErrorTokenMissing"),
        DomainErrorKind.AnthropicUnauthorized => LocalizationService.Get("ErrorAnthropicUnauthorized"),
        DomainErrorKind.AnthropicRateLimited when error.RetryAfterSeconds.HasValue =>
            LocalizationService.Format(
                "ErrorAnthropicRateLimitedWithDelay",
                Math.Max(1, (int)Math.Ceiling(error.RetryAfterSeconds.Value / 60))),
        DomainErrorKind.AnthropicRateLimited => LocalizationService.Get("ErrorAnthropicRateLimited"),
        DomainErrorKind.AnthropicHttp => LocalizationService.Format(
            "ErrorAnthropicHttp",
            error.StatusCode),
        DomainErrorKind.CodexNotFound => LocalizationService.Get("ErrorCodexNotFound"),
        DomainErrorKind.CodexProcessExited => LocalizationService.Get("ErrorCodexProcessExited"),
        DomainErrorKind.CodexRpcError => LocalizationService.Get("ErrorCodexRpc"),
        DomainErrorKind.CodexUnauthorized => LocalizationService.Get("ErrorCodexUnauthorized"),
        DomainErrorKind.Decoding => LocalizationService.Get("ErrorDecoding"),
        DomainErrorKind.Timeout => LocalizationService.Get("ErrorTimeout"),
        DomainErrorKind.Network => LocalizationService.Get("ErrorNetwork"),
        DomainErrorKind.AgyNotFound => LocalizationService.Get("ErrorAgyNotFound"),
        DomainErrorKind.AgyUnsupportedVersion => LocalizationService.Get("ErrorAgyUnsupportedVersion"),
        DomainErrorKind.AgyCommandFailed => LocalizationService.Get("ErrorAgyCommandFailed"),
        _ => LocalizationService.Get("AppUnexpectedError"),
    };

    public static string PollingInterval(PollingInterval interval) => interval switch
    {
        Utilities.PollingInterval.Sec30 => LocalizationService.Format("DurationSeconds", 30),
        Utilities.PollingInterval.Min1 => LocalizationService.Format("DurationMinutes", 1),
        Utilities.PollingInterval.Min2 => LocalizationService.Format("DurationMinutes", 2),
        Utilities.PollingInterval.Min3 => LocalizationService.Format("DurationMinutes", 3),
        Utilities.PollingInterval.Min5 => LocalizationService.Format("DurationMinutes", 5),
        Utilities.PollingInterval.Min10 => LocalizationService.Format("DurationMinutes", 10),
        _ => interval.ToString(),
    };

    public static string PopupTransparency(PopupTransparency transparency) => transparency switch
    {
        Utilities.PopupTransparency.Percent0 => LocalizationService.Get("TransparencyOpaque"),
        Utilities.PopupTransparency.Percent40 => LocalizationService.Get("TransparencyLight"),
        _ => $"{transparency.ToPercent()}%",
    };

    public static string AppTheme(AppTheme theme) => theme switch
    {
        Utilities.AppTheme.Light => LocalizationService.Get("ThemeLight"),
        Utilities.AppTheme.Dark => LocalizationService.Get("ThemeDark"),
        _ => LocalizationService.Get("ThemeSystem"),
    };

    public static string ResetTime(DateTime resetsAt)
    {
        if (resetsAt == DateTime.MinValue) return LocalizationService.Get("ResetNoRecentUsage");

        var local = resetsAt.Kind == DateTimeKind.Utc ? resetsAt.ToLocalTime() : resetsAt;
        var now = DateTime.Now;
        if (local <= now.AddMinutes(1)) return LocalizationService.Get("ResetSoon");

        var difference = local - now;
        if (difference.TotalDays >= 1)
        {
            return LocalizationService.Format(
                "ResetInDaysHours",
                (int)difference.TotalDays,
                difference.Hours,
                local.ToString("g", LocalizationService.Culture));
        }

        var hours = (int)difference.TotalHours;
        return hours > 0
            ? LocalizationService.Format(
                "ResetInHoursMinutes",
                hours,
                difference.Minutes,
                local.ToString("t", LocalizationService.Culture))
            : LocalizationService.Format(
                "ResetInMinutes",
                difference.Minutes,
                local.ToString("t", LocalizationService.Culture));
    }
}
