namespace UpdatesCowork.Shared.Contracts;

/// <summary>画面が表示中の更新情報ページ(6.1節、6.2節)。</summary>
public sealed record PageContext(
    string? UpstreamUrl,
    string? UpdateId,
    string? Title,
    string? Lang);

/// <summary><c>POST /api/chat</c> の本文。</summary>
public sealed record ChatRequest(
    string? ThreadId,
    string? Message,
    string? Action,
    PageContext? PageContext);

/// <summary><c>POST /api/chat/approval</c> の本文。</summary>
public sealed record ApprovalRequest(
    string? ThreadId,
    string? ApprovalRequestId,
    bool Approve);

/// <summary><c>POST /api/feedback</c> の本文。</summary>
public sealed record FeedbackRequest(
    string? ThreadId,
    string? ResponseId,
    string? Rating,
    string? Comment);

/// <summary><c>POST /api/threads/new</c> の応答。</summary>
public sealed record NewThreadResponse(string ThreadId);

/// <summary>画面の動作ボタン(<c>action</c>)の値。</summary>
public static class ChatActions
{
    public const string Summarize = "summarize";
    public const string Word = "word";
    public const string Pptx = "pptx";
    public const string Related = "related";

    public static IReadOnlyList<string> All { get; } = [Summarize, Word, Pptx, Related];

    /// <summary><c>null</c> か既知の値なら <c>true</c>。</summary>
    public static bool IsKnown(string? action) =>
        action is null || All.Contains(action, StringComparer.Ordinal);
}

/// <summary>評価ボタンの値。</summary>
public static class FeedbackRatings
{
    public const string Good = "good";
    public const string Bad = "bad";

    public static bool IsKnown(string? rating) => rating is Good or Bad;
}

/// <summary><c>GET /api/config</c> の応答。</summary>
public sealed record ClientConfig(
    string AuthMode,
    ClientFeatures Features);

/// <summary>画面で有効にする機能。</summary>
public sealed record ClientFeatures(bool Approvals, bool LearningMode);
