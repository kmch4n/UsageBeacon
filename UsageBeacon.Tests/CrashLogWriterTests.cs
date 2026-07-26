using System.Text;
using System.Text.RegularExpressions;
using UsageBeacon.Services;

namespace UsageBeacon.Tests;

public sealed class CrashLogWriterTests
{
    // Surface

    [Fact]
    public void Constructor_DefaultsToTheLocalAppDataLogsDirectory()
    {
        var writer = new CrashLogWriter();

        Assert.Equal(Path.Combine(AppDataPaths.LocalDirectoryPath, "logs"), writer.DirectoryPath);
        Assert.Equal(Path.Combine(writer.DirectoryPath, "crash.log"), writer.CurrentFilePath);
    }

    [Fact]
    public void Constructor_DoesNotCreateTheDirectory()
    {
        using var directory = new TempDirectory();
        var logs = Path.Combine(directory.Path, "logs");

        _ = new CrashLogWriter(logs);

        // A healthy install must never grow an empty logs directory.
        Assert.False(Directory.Exists(logs));
    }

    // Writing

    [Fact]
    public void Write_CreatesTheDirectoryAndRecordsTypeMessageAndStack()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("Dispatcher", Caught(() => throw new InvalidOperationException("boom")));

        var text = File.ReadAllText(writer.CurrentFilePath);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("boom", text);
        Assert.Contains("   at ", text);
    }

    [Fact]
    public void Write_AppendsWithoutDiscardingEarlierRecords()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("Dispatcher", new InvalidOperationException("first failure"));
        writer.Write("AppDomain", new InvalidOperationException("second failure"));

        var text = File.ReadAllText(writer.CurrentFilePath);
        Assert.Contains("first failure", text);
        Assert.Contains("second failure", text);
    }

    [Fact]
    public void Write_UsesUtf8WithoutBom()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("Dispatcher", new InvalidOperationException("日本語のメッセージ"));

        var bytes = File.ReadAllBytes(writer.CurrentFilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Contains("日本語のメッセージ", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Write_IncludesTheSourceTagAndAnIsoTimestamp()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("TaskScheduler", new InvalidOperationException("boom"));

        var text = File.ReadAllText(writer.CurrentFilePath);
        Assert.Contains("TaskScheduler", text);
        Assert.Matches(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z", text);
    }

    [Fact]
    public void Write_RecordsTheInnerExceptionChain()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));
        var inner = new IOException("inner cause");
        var outer = new InvalidOperationException("outer cause", inner);

        writer.Write("Dispatcher", outer);

        var text = File.ReadAllText(writer.CurrentFilePath);
        Assert.Contains("outer cause", text);
        Assert.Contains("inner cause", text);
    }

    // Redaction

    [Fact]
    public void Redact_ReplacesTheUserProfileDirectoryWithAPlaceholder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var redacted = CrashLogWriter.Redact(
            $"{profile}\\a.txt and {profile.Replace('\\', '/')}/b.txt and {profile.ToUpperInvariant()}\\c.txt");

        Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", redacted);
    }

    [Fact]
    public void Redact_ReplacesTheAccountNameOutsideProfilePaths()
    {
        var user = Environment.UserName;

        var redacted = CrashLogWriter.Redact($@"\\server\home\{user}\notes.txt");

        Assert.DoesNotContain(user, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USER%", redacted);
    }

    [Fact]
    public void Redact_LeavesTypeNamesIntact_WhenTheAccountNameAppearsInsideThem()
    {
        // A blind substring replace would mangle the stack trace this feature exists to keep.
        var frame = $"   at UsageBeacon.Services.{Environment.UserName}Helper.Run()";

        var redacted = CrashLogWriter.Redact(frame);

        Assert.Equal(frame, redacted);
    }

    [Fact]
    public void Redact_MasksApiKeysBearerTokensAndJwtValues()
    {
        var redacted = CrashLogWriter.Redact(
            "key sk-ant-api03-ABCDEFGHIJKLMNOP; Authorization: Bearer abcdefghijklmnopqrst; " +
            "refresh_token=rt_abcdefghijklmnop; eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NX0.SflKxwRJSM");

        Assert.DoesNotContain("sk-ant-api03-ABCDEFGHIJKLMNOP", redacted);
        Assert.DoesNotContain("abcdefghijklmnopqrst", redacted);
        Assert.DoesNotContain("rt_abcdefghijklmnop", redacted);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted);
        Assert.Contains("<redacted>", redacted);
    }

    [Fact]
    public void Redact_LeavesOrdinaryProseAndFramesUnchanged()
    {
        const string text = "   at UsageBeacon.Services.TokenSource.Refresh()\nthe token expired";

        Assert.Equal(text, CrashLogWriter.Redact(text));
    }

    [Fact]
    public void Write_RedactsTheRenderedRecord()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        writer.Write("Dispatcher", new InvalidOperationException(
            $"failed reading {profile}\\creds.json with sk-ant-api03-ABCDEFGHIJKLMNOP"));

        var text = File.ReadAllText(writer.CurrentFilePath);
        Assert.DoesNotContain(profile, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-ant-api03-ABCDEFGHIJKLMNOP", text);
        Assert.Contains("%USERPROFILE%", text);
        Assert.Contains("<redacted>", text);
    }

    // Rotation

    [Fact]
    public void Write_TruncatesOversizedRecords()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("Dispatcher", new InvalidOperationException(new string('x', 1_000_000)));

        var length = new FileInfo(writer.CurrentFilePath).Length;
        Assert.True(length <= CrashLogWriter.MaxRecordBytes * 2, $"record was {length} bytes");
    }

    [Fact]
    public void Write_RotatesTheCurrentFile_WhenItExceedsTheSizeCap()
    {
        using var directory = new TempDirectory();
        var logs = Path.Combine(directory.Path, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllText(Path.Combine(logs, "crash.log"), new string('o', (int)CrashLogWriter.MaxFileBytes + 1));
        var writer = new CrashLogWriter(logs, () => new DateTime(2026, 7, 27, 1, 2, 3, DateTimeKind.Utc));

        writer.Write("Dispatcher", new InvalidOperationException("fresh failure"));

        var archives = Directory.GetFiles(logs, "crash-*.log");
        Assert.Single(archives);
        Assert.Contains("oooo", File.ReadAllText(archives[0]));
        Assert.Contains("fresh failure", File.ReadAllText(writer.CurrentFilePath));
    }

    [Fact]
    public void Write_KeepsAtMostFiveLogFiles()
    {
        using var directory = new TempDirectory();
        var logs = Path.Combine(directory.Path, "logs");
        var clock = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);
        var writer = new CrashLogWriter(logs, () => clock);

        for (var i = 0; i < 8; i++)
        {
            clock = clock.AddMinutes(1);
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "crash.log"), new string('o', (int)CrashLogWriter.MaxFileBytes + 1));
            writer.Write("Dispatcher", new InvalidOperationException($"failure {i}"));
        }

        Assert.Equal(CrashLogWriter.MaxArchivedFiles + 1, Directory.GetFiles(logs, "crash*.log").Length);
    }

    // Resilience

    [Fact]
    public void Write_DoesNotThrow_WhenTheLogPathIsUnusable()
    {
        using var directory = new TempDirectory();
        var blocker = Path.Combine(directory.Path, "blocker");
        File.WriteAllText(blocker, "not a directory");
        var writer = new CrashLogWriter(Path.Combine(blocker, "logs"));

        writer.Write("Dispatcher", new InvalidOperationException("boom"));
    }

    [Fact]
    public void Write_DoesNotThrow_WhenTheExceptionCannotBeRendered()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        writer.Write("Dispatcher", new UnrenderableException());
    }

    [Fact]
    public void Write_DoesNotThrow_WhenTheCurrentFileIsLockedByAnotherHandle()
    {
        using var directory = new TempDirectory();
        var logs = Path.Combine(directory.Path, "logs");
        Directory.CreateDirectory(logs);
        var path = Path.Combine(logs, "crash.log");
        File.WriteAllText(path, "existing");
        var writer = new CrashLogWriter(logs);

        using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        writer.Write("Dispatcher", new InvalidOperationException("boom"));
    }

    [Fact]
    public void Write_DoesNotThrow_WhenRedactionTimesOut()
    {
        using var directory = new TempDirectory();
        var writer = new CrashLogWriter(Path.Combine(directory.Path, "logs"));

        // RegexMatchTimeoutException derives from TimeoutException, so an outer
        // guard that only catches IO failures would let it escape a dying process.
        writer.Write("Dispatcher", new InvalidOperationException(PathologicalRedactionInput()));
    }

    private static string PathologicalRedactionInput() =>
        "Authorization: " + new string('a', 200_000);

    private static Exception Caught(Action action)
    {
        try
        {
            action();
            throw new InvalidOperationException("the action was expected to throw");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private sealed class UnrenderableException : Exception
    {
        public override string ToString() => throw new NotSupportedException("cannot render");

        public override string Message => throw new NotSupportedException("cannot render");
    }

    private sealed class TempDirectory : IDisposable
    {
        private readonly DirectoryInfo _directory =
            Directory.CreateTempSubdirectory("UsageBeaconTests-");

        public string Path => _directory.FullName;

        public void Dispose()
        {
            try { _directory.Delete(recursive: true); } catch { }
        }
    }
}
