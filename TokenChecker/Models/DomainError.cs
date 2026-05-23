namespace TokenChecker.Models;

public enum DomainErrorKind
{
    TokenMissing,
    AnthropicUnauthorized,
    AnthropicRateLimited,
    AnthropicHttp,
    CodexNotFound,
    CodexProcessExited,
    CodexRpcError,
    Decoding,
    Timeout,
    Network,
}

public sealed class DomainError : Exception
{
    public DomainErrorKind Kind { get; }
    public double? RetryAfterSeconds { get; }

    private DomainError(DomainErrorKind kind, string message, double? retryAfterSeconds = null) : base(message)
    {
        Kind = kind;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public static DomainError TokenMissing() => new(DomainErrorKind.TokenMissing,
        "Claude Code の認証情報が見つかりません。`claude login` でログインしてください。");

    public static DomainError AnthropicUnauthorized() => new(DomainErrorKind.AnthropicUnauthorized,
        "Claude の認証が期限切れです。`claude login` で再ログインしてください。");

    public static DomainError AnthropicRateLimited(double? retryAfterSecs) => new(DomainErrorKind.AnthropicRateLimited,
        retryAfterSecs.HasValue
            ? $"Claude はログイン済みです。使用量 API の制限中のため、約 {Math.Max(1, (int)Math.Ceiling(retryAfterSecs.Value / 60))} 分後に再取得します。"
            : "Claude はログイン済みです。使用量 API の制限中のため、再取得を待機します。",
        retryAfterSecs);

    public static DomainError AnthropicHttp(int status) => new(DomainErrorKind.AnthropicHttp,
        $"Anthropic API エラー (status {status})");

    public static DomainError CodexNotFound() => new(DomainErrorKind.CodexNotFound,
        "Codex CLI が見つかりません。`npm i -g @openai/codex` を実行してください。");

    public static DomainError CodexProcessExited() => new(DomainErrorKind.CodexProcessExited,
        "codex app-server が終了しました。再起動を試みます。");

    public static DomainError CodexRpcError(string msg) => new(DomainErrorKind.CodexRpcError,
        $"Codex RPC エラー: {msg}");

    public static DomainError Decoding(string detail) => new(DomainErrorKind.Decoding,
        $"レスポンスのデコードに失敗: {detail}");

    public static DomainError Timeout() => new(DomainErrorKind.Timeout,
        "通信がタイムアウトしました。");

    public static DomainError Network(string detail) => new(DomainErrorKind.Network,
        $"ネットワークエラー: {detail}");
}
