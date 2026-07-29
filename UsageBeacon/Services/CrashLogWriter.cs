using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace UsageBeacon.Services;

/// <summary>
/// Appends redacted crash records to a rotating, size-capped local log.
/// Nothing is transmitted: the file exists so a bug report can carry a stack
/// trace that the error dialog deliberately withholds. Records are redacted for
/// the user profile path, the account name, and credential-shaped values before
/// they reach disk, and every failure inside the writer is swallowed so it can
/// never mask the exception that is being reported.
/// </summary>
public sealed class CrashLogWriter
{
    /// <summary>Maximum size of a single crash record.</summary>
    public const int MaxRecordBytes = 16 * 1024;

    /// <summary>Size at which the current log is rotated.</summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>Archived logs kept alongside the current one.</summary>
    public const int MaxArchivedFiles = 4;

    private const string CurrentFileName = "crash.log";
    private const string ArchivePattern = "crash-*.log";
    private const string TruncationMarker = "... <truncated>";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan RedactionTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly Regex[] SecretPatterns =
    {
        new(@"sk-ant-[A-Za-z0-9_\-]{8,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, RedactionTimeout),
        new(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, RedactionTimeout),
        new(@"eyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}(\.[A-Za-z0-9_\-]+)?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant, RedactionTimeout),
        // The optional scheme keeps "Authorization: Bearer <value>" from stopping at "Bearer".
        new(@"\b(authorization|api[_-]?key|access[_-]?token|refresh[_-]?token|token|password|secret)\b\s*[:=]\s*(?:bearer\s+)?\S+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RedactionTimeout),
        new(@"\bbearer\s+[A-Za-z0-9._~+/=\-]{8,}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RedactionTimeout),
    };

    private readonly Func<DateTime> _utcNow;
    private readonly Func<string, string> _redact;

    public CrashLogWriter(string? directoryPath = null, Func<DateTime>? utcNow = null)
        : this(directoryPath, utcNow, Redact)
    {
    }

    internal CrashLogWriter(
        string? directoryPath,
        Func<DateTime>? utcNow,
        Func<string, string> redact)
    {
        // Deliberately no directory creation: a healthy install never grows a logs folder.
        DirectoryPath = directoryPath ?? Path.Combine(AppDataPaths.LocalDirectoryPath, "logs");
        CurrentFilePath = Path.Combine(DirectoryPath, CurrentFileName);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _redact = redact;
    }

    public string DirectoryPath { get; }

    public string CurrentFilePath { get; }

    /// <summary>Appends one redacted crash record. Never throws.</summary>
    public void Write(string source, Exception exception)
    {
        try
        {
            string record;
            try
            {
                record = _redact(FormatRecord(source, exception));
            }
            catch (RegexMatchTimeoutException)
            {
                record = FormatRedactionFailure(exception);
            }

            Directory.CreateDirectory(DirectoryPath);
            RotateIfNeeded();
            File.AppendAllText(CurrentFilePath, record, Utf8NoBom);
        }
        catch
        {
            // Diagnostics must never become the reason a crash is lost or a
            // shutdown path fails. There is nowhere left to report this to.
        }
    }

    /// <summary>Removes local identity and credential-shaped values from a record.</summary>
    internal static string Redact(string text)
    {
        var redacted = ReplaceProfilePaths(text);
        redacted = ReplaceAccountName(redacted);

        foreach (var pattern in SecretPatterns)
            redacted = ReplaceSecrets(pattern, redacted);

        return Truncate(redacted);
    }

    private static string ReplaceProfilePaths(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile)) return text;

        var redacted = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        return redacted.Replace(profile.Replace('\\', '/'), "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReplaceAccountName(string text)
    {
        var user = Environment.UserName;
        if (string.IsNullOrEmpty(user)) return text;

        // Word-boundary anchored: an ordinary short account name such as "Max"
        // also occurs inside .NET namespaces, and a substring replace would
        // corrupt the stack trace this log exists to preserve.
        return Regex.Replace(
            text,
            $@"(?<![A-Za-z0-9_]){Regex.Escape(user)}(?![A-Za-z0-9_])",
            "%USER%",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            RedactionTimeout);
    }

    private static string ReplaceSecrets(Regex pattern, string text)
    {
        return pattern.Replace(text, match =>
        {
            // Keep the key name so the log still says which value was masked.
            var separator = match.Value.IndexOfAny(new[] { ':', '=' });
            return separator > 0
                ? match.Value[..(separator + 1)] + " <redacted>"
                : "<redacted>";
        });
    }

    private static string Truncate(string text)
        => text.Length <= MaxRecordBytes ? text : text[..MaxRecordBytes] + TruncationMarker + Environment.NewLine;

    private string FormatRecord(string source, Exception exception)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var header = $"==== {_utcNow():yyyy-MM-ddTHH:mm:ss.fffZ} | {source} | UsageBeacon {version} | {Environment.OSVersion} ====";
        return header + Environment.NewLine + exception + Environment.NewLine + Environment.NewLine;
    }

    private string FormatRedactionFailure(Exception exception)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        var exceptionType = exception.GetType().FullName ?? "unknown";
        var header = $"==== {_utcNow():yyyy-MM-ddTHH:mm:ss.fffZ} | CrashLogWriter | UsageBeacon {version} ====";
        return header + Environment.NewLine +
               $"Redaction timed out; original crash details omitted. Exception type: {exceptionType}" +
               Environment.NewLine + Environment.NewLine;
    }

    private void RotateIfNeeded()
    {
        var current = new FileInfo(CurrentFilePath);
        if (!current.Exists || current.Length < MaxFileBytes) return;

        var archive = Path.Combine(DirectoryPath, $"crash-{_utcNow():yyyyMMdd-HHmmss}.log");
        File.Move(CurrentFilePath, archive, overwrite: true);
        PruneArchives();
    }

    private void PruneArchives()
    {
        // Archive names are timestamps, so ordinal order is chronological order.
        var stale = Directory.GetFiles(DirectoryPath, ArchivePattern)
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(MaxArchivedFiles);

        foreach (var path in stale)
        {
            try { File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
