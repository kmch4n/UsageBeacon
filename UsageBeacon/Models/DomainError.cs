namespace UsageBeacon.Models;

public enum DomainErrorKind
{
    TokenMissing,
    AnthropicUnauthorized,
    AnthropicRateLimited,
    AnthropicHttp,
    CodexNotFound,
    CodexProcessExited,
    CodexRpcError,
    CodexUnauthorized,
    Decoding,
    Timeout,
    Network,
    AgyNotFound,
    AgyUnsupportedVersion,
    AgyCommandFailed,
}

public sealed class DomainError : Exception
{
    public DomainErrorKind Kind { get; }
    public double? RetryAfterSeconds { get; }
    public int? StatusCode { get; }
    public string? Detail { get; }

    private DomainError(
        DomainErrorKind kind,
        string message,
        double? retryAfterSeconds = null,
        int? statusCode = null,
        string? detail = null) : base(message)
    {
        Kind = kind;
        RetryAfterSeconds = retryAfterSeconds;
        StatusCode = statusCode;
        Detail = detail;
    }

    public static DomainError TokenMissing() => new(DomainErrorKind.TokenMissing,
        "Claude Code credentials were not found.");

    public static DomainError AnthropicUnauthorized() => new(DomainErrorKind.AnthropicUnauthorized,
        "Claude authentication has expired.");

    public static DomainError AnthropicRateLimited(double? retryAfterSecs) => new(DomainErrorKind.AnthropicRateLimited,
        "The Claude usage API is rate limited.",
        retryAfterSecs);

    public static DomainError AnthropicHttp(int status) => new(DomainErrorKind.AnthropicHttp,
        $"Anthropic API error (status {status}).",
        statusCode: status);

    public static DomainError CodexNotFound() => new(DomainErrorKind.CodexNotFound,
        "Codex CLI was not found.");

    public static DomainError CodexProcessExited() => new(DomainErrorKind.CodexProcessExited,
        "The Codex app-server exited.");

    public static DomainError CodexRpcError(string msg) => new(DomainErrorKind.CodexRpcError,
        $"Codex RPC error: {msg}",
        detail: msg);

    public static DomainError CodexUnauthorized() => new(DomainErrorKind.CodexUnauthorized,
        "Codex authentication has expired.");

    public static DomainError Decoding(string detail) => new(DomainErrorKind.Decoding,
        $"Response decoding failed: {detail}",
        detail: detail);

    public static DomainError Timeout() => new(DomainErrorKind.Timeout,
        "The request timed out.");

    public static DomainError Network(string detail) => new(DomainErrorKind.Network,
        $"Network error: {detail}",
        detail: detail);

    public static DomainError AgyNotFound() => new(DomainErrorKind.AgyNotFound,
        "Antigravity CLI (agy) was not found.");

    public static DomainError AgyUnsupportedVersion(string? version) => new(DomainErrorKind.AgyUnsupportedVersion,
        $"Antigravity CLI {version ?? "(unknown version)"} cannot report usage without a model prompt.",
        detail: version);

    public static DomainError AgyCommandFailed(string detail) => new(DomainErrorKind.AgyCommandFailed,
        $"Antigravity usage command failed: {detail}",
        detail: detail);
}
