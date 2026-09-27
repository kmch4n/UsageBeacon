using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsageBeacon.Models;

namespace UsageBeacon.Services;

/// <summary>
/// Spawns `codex app-server` and communicates through JSON-RPC.
/// Ports the macOS client design to Windows.
/// </summary>
public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly string[] _candidates;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    private Process?      _process;
    private StreamWriter? _stdin;
    private CancellationTokenSource? _readCts;
    private bool _started;
    private volatile int _generation;
    private volatile string _lastStderr = "";

    /// <summary>Most recent non-empty stderr line from codex app-server.</summary>
    public string LastStderr => _lastStderr;

    private readonly object _writeLock = new();
    private readonly object _pendingLock = new();
    private int _nextId = 1;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    public CodexAppServerClient(string[]? candidates = null, TimeSpan? timeout = null)
    {
        _candidates = candidates ?? DefaultCandidates();
        _timeout    = timeout ?? TimeSpan.FromSeconds(8);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _startLock.WaitAsync(ct);
        try
        {
            if (_started && _process?.HasExited == false) return;

            // Clean up a process left by the previous generation.
            if (_started)
            {
                _readCts?.Cancel();
                _stdin?.Dispose();
                _stdin = null;
                try { _process?.Dispose(); } catch { }
                _process = null;
                _started = false;
            }

            var exe = ResolveExecutable() ?? throw DomainError.CodexNotFound();

            _process = CreateProcess(exe);
            var myGen = Interlocked.Increment(ref _generation);
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, _) => { if (myGen == _generation) FailAll(DomainError.CodexProcessExited()); };
            _process.Start();

            // Codex expects UTF-8 without a BOM.
            // JSON-RPC uses line framing, so force LF to avoid CRLF parsing issues.
            _stdin = new StreamWriter(
                _process.StandardInput.BaseStream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            { AutoFlush = true, NewLine = "\n" };

            _started  = true;
            _readCts  = new CancellationTokenSource();
            var readToken = _readCts.Token;
            var stdout    = _process.StandardOutput;
            var stderr    = _process.StandardError;
            _ = Task.Run(() => ReadLoopAsync(stdout, myGen, readToken), readToken);
            // Drain stderr so a full pipe cannot block the child process.
            _ = Task.Run(() => DrainStderrAsync(stderr, readToken), readToken);

            // Initialize handshake. A failed handshake leaves the child alive,
            // so discard it before a later StartAsync can reuse it.
            try
            {
                _ = await SendRequestAsync("initialize", new
                {
                    clientInfo   = new { name = "usage-beacon", version = "0.1.0" },
                    capabilities = new { }
                }, ct);
                SendNotification("initialized", new { });
            }
            catch
            {
                Stop();
                throw;
            }
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<CodexRateLimitsDto> ReadRateLimitsAsync(CancellationToken ct = default)
    {
        var result = await SendRequestAsync("account/rateLimits/read", new { }, ct);
        if (result.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw DomainError.CodexRpcError("missing result");
        try
        {
            return result.Deserialize<CodexRateLimitsDto>(JsonOpts)
                   ?? throw DomainError.CodexRpcError("null result");
        }
        catch (JsonException e)
        {
            throw DomainError.Decoding($"codex rateLimits: {e.Message}");
        }
    }

    public void Stop()
    {
        // Advance the generation so callbacks from the stopped process cannot fail later requests.
        Interlocked.Increment(ref _generation);
        _readCts?.Cancel();
        try { if (_process?.HasExited == false) _process.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process  = null;
        _stdin?.Dispose();
        _stdin    = null;
        _started  = false;
        FailAll(DomainError.CodexProcessExited());
    }

    // ── Internal I/O ────────────────────────────────────────────────────

    private async Task ReadLoopAsync(StreamReader reader, int generation, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    ProcessLine(line);
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { if (generation == _generation) FailAll(DomainError.CodexProcessExited()); }
    }

    private async Task DrainStderrAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line)) _lastStderr = line.Trim();
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    // Detect authentication expiry in rate-limit errors returned by codex app-server.
    private static bool IsAuthError(string message) =>
        message.Contains("token_invalidated", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("401") ||
        message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("invalidated", StringComparison.OrdinalIgnoreCase);

    private void ProcessLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("id", out var idEl) ||
                idEl.ValueKind != JsonValueKind.Number) return;

            var id = idEl.GetInt32();

            // Handle JSON-RPC error responses before decoding a result.
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m)
                    ? m.GetString() ?? "RPC error" : "RPC error";
                var error = IsAuthError(msg)
                    ? DomainError.CodexUnauthorized()
                    : DomainError.CodexRpcError(msg);
                lock (_pendingLock)
                {
                    if (_pending.TryGetValue(id, out var tcs))
                    {
                        _pending.Remove(id);
                        tcs.TrySetException(error);
                    }
                }
                return;
            }

            var result = doc.RootElement.TryGetProperty("result", out var r) ? r.Clone() : default;

            lock (_pendingLock)
            {
                if (_pending.TryGetValue(id, out var tcs))
                {
                    _pending.Remove(id);
                    tcs.TrySetResult(result);
                }
            }
        }
        catch { }
    }

    private async Task<JsonElement> SendRequestAsync<T>(string method, T @params, CancellationToken ct)
    {
        int id;
        lock (_pendingLock) { id = _nextId++; }

        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingLock) { _pending[id] = tcs; }

        var json = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id, method, @params }, JsonOpts);

        // Snapshot stdin locally because Stop can clear the field concurrently.
        var stdin = _stdin;
        if (stdin == null)
        {
            lock (_pendingLock) { _pending.Remove(id); }
            throw DomainError.CodexProcessExited();
        }
        try
        {
            lock (_writeLock) { stdin.WriteLine(json); }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            lock (_pendingLock) { _pending.Remove(id); }
            throw DomainError.CodexProcessExited();
        }

        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        await using var reg  = linked.Token.Register(() =>
        {
            lock (_pendingLock) { _pending.Remove(id); }
            tcs.TrySetException(timeoutCts.IsCancellationRequested
                ? (Exception)DomainError.Timeout()
                : new OperationCanceledException(ct));
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private void SendNotification<T>(string method, T @params)
    {
        var json = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", method, @params }, JsonOpts);
        var stdin = _stdin;
        if (stdin == null) return;
        lock (_writeLock) { stdin.WriteLine(json); }
    }

    private void FailAll(DomainError error)
    {
        List<TaskCompletionSource<JsonElement>> snapshot;
        lock (_pendingLock)
        {
            snapshot = [.. _pending.Values];
            _pending.Clear();
        }
        foreach (var tcs in snapshot) tcs.TrySetException(error);
    }

    // ── Process setup ───────────────────────────────────────────────────

    private static Process CreateProcess(string exe)
    {
        string fileName, args;

        // Run .cmd and .bat launchers through cmd.exe.
        if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
            exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            // Reject characters that retain special meaning inside cmd.exe quotes.
            // Percent expands environment variables and a quote terminates the quoted path.
            if (exe.IndexOfAny(['"', '%']) >= 0)
                throw new ArgumentException($"Executable path contains an unsafe character: {exe}");
            fileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            args     = $"/c \"{exe}\" app-server";
        }
        else
        {
            fileName = exe;
            args     = "app-server";
        }

        var psi = new ProcessStartInfo(fileName, args)
        {
            UseShellExecute          = false,
            RedirectStandardInput    = true,
            RedirectStandardOutput   = true,
            RedirectStandardError    = true,
            CreateNoWindow           = true,
            StandardOutputEncoding   = Encoding.UTF8,
            StandardErrorEncoding    = Encoding.UTF8,
        };

        // Pass only the environment variables required by the child process.
        psi.EnvironmentVariables.Clear();
        var keep = new[]
        {
            "PATH", "PATHEXT", "USERPROFILE", "HOME",
            "APPDATA", "LOCALAPPDATA", "TEMP", "TMP",
            "USERNAME", "COMSPEC", "SYSTEMROOT", "SYSTEMDRIVE",
            "CODEX_HOME",
        };
        foreach (var k in keep)
        {
            var v = Environment.GetEnvironmentVariable(k);
            if (v != null) psi.EnvironmentVariables[k] = v;
        }

        return new Process { StartInfo = psi };
    }

    private string? ResolveExecutable()
    {
        foreach (var c in _candidates)
            if (File.Exists(c)) return c;

        // Search PATH with where.exe and validate the extension before execution.
        // Use the System32 binary so a forged where.exe earlier on PATH cannot be selected.
        try
        {
            var whereExe = Path.Combine(Environment.SystemDirectory, "where.exe");
            using var p = Process.Start(new ProcessStartInfo(whereExe, "codex")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                CreateNoWindow         = true,
            })!;
            var outputTask = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(2000))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            var lines = outputTask.GetAwaiter().GetResult()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var line in lines)
                if (File.Exists(line) && IsAllowedExtension(line))
                    return line;
        }
        catch { }

        return null;
    }

    private static bool IsAllowedExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bat", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] DefaultCandidates()
    {
        var appData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates   = new List<string>();
        var nvmSymlink   = Environment.GetEnvironmentVariable("NVM_SYMLINK");
        if (!string.IsNullOrWhiteSpace(nvmSymlink))
            candidates.Add(Path.Combine(nvmSymlink, "codex.cmd"));
        candidates.AddRange(
        [
            Path.Combine(appData, "npm", "codex.cmd"),
            Path.Combine(appData, "Roaming", "npm", "codex.cmd"),
            Path.Combine(programFiles, "nodejs", "codex.cmd"),
            Path.Combine(programFiles + " (x86)", "nodejs", "codex.cmd"),
        ]);
        return [.. candidates];
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────────

public sealed class CodexRateLimitsDto
{
    [JsonPropertyName("rateLimits")]
    public RateLimitSnapshot? RateLimits { get; init; }

    [JsonPropertyName("rateLimitsByLimitId")]
    public Dictionary<string, RateLimitSnapshot>? RateLimitsByLimitId { get; init; }

    public RateLimit? FiveHourRateLimit() => Window(300);
    public RateLimit? WeeklyRateLimit()   => Window(10080);

    private RateLimit? Window(long minutes)
    {
        var all = new List<RateLimitWindow>();
        if (RateLimits?.Primary   != null) all.Add(RateLimits.Primary);
        if (RateLimits?.Secondary != null) all.Add(RateLimits.Secondary);
        foreach (var s in (RateLimitsByLimitId ?? []).Values)
        {
            if (s.Primary   != null) all.Add(s.Primary);
            if (s.Secondary != null) all.Add(s.Secondary);
        }
        var w = all.FirstOrDefault(x => x.WindowDurationMins == minutes);
        if (w == null) return null;
        return new RateLimit(
            Utilization: Math.Max(0, (w.UsedPercent ?? 0) / 100.0),
            // DateTime.MinValue is the UI sentinel for "no reset time reported".
            ResetsAt:    w.ResetsAt is { } resetsAt
                ? DateTimeOffset.FromUnixTimeSeconds(resetsAt).LocalDateTime
                : DateTime.MinValue);
    }
}

public sealed class RateLimitSnapshot
{
    [JsonPropertyName("limitId")]   public string?          LimitId   { get; init; }
    [JsonPropertyName("primary")]   public RateLimitWindow? Primary   { get; init; }
    [JsonPropertyName("secondary")] public RateLimitWindow? Secondary { get; init; }
    [JsonPropertyName("planType")]  public string?          PlanType  { get; init; }
}

public sealed class RateLimitWindow
{
    // The v2 app-server protocol serializes an integer today; accept fractional
    // percentages as well because the core codex protocol defines them as f64.
    [JsonPropertyName("usedPercent")]        public double? UsedPercent      { get; init; }
    [JsonPropertyName("windowDurationMins")] public long? WindowDurationMins { get; init; }
    [JsonPropertyName("resetsAt")]           public long? ResetsAt           { get; init; }
}
