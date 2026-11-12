using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Web.Chat;

/// <summary>検証済みのページ情報。<see cref="Locale"/> は <c>ja-JP</c> の形にそろえた値。</summary>
internal sealed record ValidatedPageContext(string? UpstreamUrl, string? UpdateId, string? Title, string? Locale);

/// <summary>検証済みの依頼。</summary>
internal sealed record ValidatedChatInput(string Message, string? Action, ValidatedPageContext? Page);

/// <summary>
/// 画面から届いた依頼とページ情報を確かめる(5.2節の手順1)。
/// ここを通った値だけを <c>&lt;page_context&gt;</c> やテレメトリに使う。
/// </summary>
internal static partial class PageContextValidator
{
    public const int MaxMessageLength = 2000;
    public const int MaxTitleLength = 300;
    public const int MaxUrlLength = 2048;
    public const string AllowedUrlPrefix = "https://azure.microsoft.com/";

    /// <summary><c>POST /api/chat</c> の本文を確かめる。違反があれば <paramref name="error"/> に利用者向けの文を入れる。</summary>
    public static bool TryValidate(ChatRequest? request, out ValidatedChatInput? input, out string? error)
    {
        input = null;
        if (request is null)
        {
            error = "要求の本文がありません。";
            return false;
        }

        if (!TryNormalizeMessage(request.Message, out var message, out error))
        {
            return false;
        }

        if (!ChatActions.IsKnown(request.Action))
        {
            error = "action の値が正しくありません。";
            return false;
        }

        if (!TryValidatePage(request.PageContext, out var page, out error))
        {
            return false;
        }

        input = new ValidatedChatInput(message, request.Action, page);
        return true;
    }

    /// <summary>依頼文の前後の空白と制御文字(改行とタブを除く)を取り除き、1〜2,000文字かを確かめる。</summary>
    public static bool TryNormalizeMessage(string? message, out string normalized, out string? error)
    {
        var builder = new StringBuilder(message?.Length ?? 0);
        foreach (var c in message ?? string.Empty)
        {
            if (c is '\n' or '\t' || !IsRemovableControl(c))
            {
                builder.Append(c);
            }
        }

        normalized = builder.ToString().Trim();
        if (normalized.Length is 0 or > MaxMessageLength)
        {
            error = $"依頼文は1〜{MaxMessageLength:N0}文字で入力してください。";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>ページ情報を確かめる。すべて空なら「ページなし」(<c>null</c>)として成功にする。</summary>
    public static bool TryValidatePage(PageContext? page, out ValidatedPageContext? result, out string? error)
    {
        result = null;
        error = null;
        if (page is null)
        {
            return true;
        }

        string? upstreamUrl = null;
        if (!string.IsNullOrWhiteSpace(page.UpstreamUrl))
        {
            upstreamUrl = page.UpstreamUrl.Trim();
            if (!IsAllowedUrl(upstreamUrl))
            {
                error = "表示中のページの URL が正しくありません。";
                return false;
            }
        }

        string? updateId = null;
        if (!string.IsNullOrWhiteSpace(page.UpdateId))
        {
            updateId = page.UpdateId.Trim();
            if (!UpdateIdPattern().IsMatch(updateId))
            {
                error = "更新情報の ID が正しくありません。";
                return false;
            }
        }

        string? title = null;
        if (!string.IsNullOrWhiteSpace(page.Title))
        {
            title = SanitizeTitle(page.Title);
            if (title.Length > MaxTitleLength)
            {
                error = $"タイトルは{MaxTitleLength}文字までです。";
                return false;
            }

            if (title.Length == 0)
            {
                title = null;
            }
        }

        string? locale = null;
        if (!string.IsNullOrWhiteSpace(page.Lang))
        {
            locale = NormalizeLocale(page.Lang.Trim());
            if (locale is null)
            {
                error = "言語の値が正しくありません。";
                return false;
            }
        }

        if (upstreamUrl is null && updateId is null && title is null && locale is null)
        {
            return true;
        }

        result = new ValidatedPageContext(upstreamUrl, updateId, title, locale);
        return true;
    }

    /// <summary><c>ja-jp</c> を <c>ja-JP</c> のように、言語は小文字、地域は大文字、文字体系は先頭だけ大文字にする。</summary>
    public static string? NormalizeLocale(string lang)
    {
        if (!LangPattern().IsMatch(lang))
        {
            return null;
        }

        var parts = lang.Split('-');
        parts[0] = parts[0].ToLowerInvariant();
        for (var i = 1; i < parts.Length; i++)
        {
            var part = parts[i];
            parts[i] = part.Length switch
            {
                2 => part.ToUpperInvariant(),
                4 => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant(),
                _ => part.ToLowerInvariant(),
            };
        }

        return string.Join('-', parts);
    }

    /// <summary>タイトルの改行とタブを空白にし、制御文字と書字方向の制御文字を取り除く。</summary>
    public static string SanitizeTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        foreach (var c in title)
        {
            if (c is '\r' or '\n' or '\t')
            {
                builder.Append(' ');
            }
            else if (!IsRemovableControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Trim();
    }

    private static bool IsAllowedUrl(string url) =>
        url.Length <= MaxUrlLength
        && url.StartsWith(AllowedUrlPrefix, StringComparison.Ordinal)
        && !url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host == "azure.microsoft.com"
        && uri.IsDefaultPort;

    private static bool IsRemovableControl(char c) =>
        char.GetUnicodeCategory(c) == UnicodeCategory.Control
        || c is >= '\u202A' and <= '\u202E'
        || c is >= '\u2066' and <= '\u2069';

    [GeneratedRegex("^[A-Za-z0-9-]{1,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex UpdateIdPattern();

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8}){0,3}$", RegexOptions.CultureInvariant)]
    private static partial Regex LangPattern();
}
