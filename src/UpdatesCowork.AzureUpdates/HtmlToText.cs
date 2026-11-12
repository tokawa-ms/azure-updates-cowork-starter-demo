using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace UpdatesCowork.AzureUpdates;

/// <summary>
/// 説明文の HTML をモデルに渡すテキストにする。段落は空行、<c>li</c> は「- 」、リンクは「文字(URL)」、
/// <c>br</c> は改行にし、<c>script</c> と <c>style</c> は捨てる。
/// </summary>
public static class HtmlToText
{
    /// <summary>既定の最大文字数。</summary>
    public const int DefaultMaxLength = 12_000;

    /// <summary>切り詰めたときに末尾に付ける文。</summary>
    public const string TruncatedSuffix = "\n(以下省略)";

    // 深すぎる入れ子はスタックを使い切らないよう読まない
    private const int MaxDepth = 128;

    private static readonly HashSet<string> SkippedElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "noscript", "template", "head", "iframe", "object", "embed", "svg", "math", "canvas",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav", "blockquote", "pre",
        "h1", "h2", "h3", "h4", "h5", "h6", "table", "ul", "ol", "dl", "figure", "hr", "address",
    };

    /// <summary>HTML をテキストにする。<paramref name="maxLength"/> を超える分は切り詰める。</summary>
    public static string Convert(string? html, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, TruncatedSuffix.Length + 1);
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var parser = new HtmlParser();
        using var document = parser.ParseDocument(html);
        var writer = new TextWriterState();
        if (document.Body is { } body)
        {
            Walk(body, writer, depth: 0, listDepth: 0);
        }

        return Truncate(Normalize(writer.Builder.ToString()), maxLength);
    }

    private static void Walk(INode node, TextWriterState writer, int depth, int listDepth)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    writer.AppendText(text.Data);
                    break;
                case IElement element:
                    WalkElement(element, writer, depth + 1, listDepth);
                    break;
            }
        }
    }

    private static void WalkElement(IElement element, TextWriterState writer, int depth, int listDepth)
    {
        var name = element.LocalName;
        if (SkippedElements.Contains(name))
        {
            return;
        }

        switch (name)
        {
            case "br":
                writer.NewLine();
                return;
            case "li":
                writer.StartBullet(Math.Max(listDepth, 1));
                Walk(element, writer, depth, listDepth);
                writer.NewLine();
                return;
            case "ul":
            case "ol":
                writer.BlankLineUnlessInList(listDepth);
                Walk(element, writer, depth, listDepth + 1);
                writer.BlankLineUnlessInList(listDepth);
                return;
            case "tr":
                writer.NewLine();
                Walk(element, writer, depth, listDepth);
                writer.NewLine();
                return;
            case "td":
            case "th":
                if (element.PreviousElementSibling is not null)
                {
                    writer.AppendRaw(" | ");
                }

                Walk(element, writer, depth, listDepth);
                return;
            case "a":
                WalkLink(element, writer, depth, listDepth);
                return;
        }

        // リストの中の段落は空行を挟まず改行だけにする
        var isBlock = BlockElements.Contains(name);
        if (isBlock)
        {
            writer.BlankLineUnlessInList(listDepth);
        }

        Walk(element, writer, depth, listDepth);

        if (isBlock)
        {
            writer.BlankLineUnlessInList(listDepth);
        }
    }

    private static void WalkLink(IElement element, TextWriterState writer, int depth, int listDepth)
    {
        var start = writer.Builder.Length;
        Walk(element, writer, depth, listDepth);
        var href = element.GetAttribute("href");
        if (!TryGetWebUrl(href, out var url))
        {
            return;
        }

        var linkText = writer.Builder.ToString(start, writer.Builder.Length - start).Trim();
        if (linkText.Length == 0)
        {
            writer.AppendText(url);
        }
        else if (!string.Equals(linkText, url, StringComparison.Ordinal))
        {
            writer.AppendRaw("(" + url + ")");
        }
    }

    private static bool TryGetWebUrl(string? href, out string url)
    {
        url = string.Empty;
        if (string.IsNullOrWhiteSpace(href)
            || !Uri.TryCreate(href.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    private static string Normalize(string text)
    {
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length);
        var blank = 0;
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                blank++;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(blank > 0 ? "\n\n" : "\n");
            }

            builder.Append(line);
            blank = 0;
        }

        return builder.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength - TruncatedSuffix.Length;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            // サロゲートペアの途中で切らない
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut).TrimEnd(), TruncatedSuffix);
    }

    /// <summary>テキストを書き出す途中の状態。</summary>
    private sealed class TextWriterState
    {
        private string? _pendingBullet;

        public StringBuilder Builder { get; } = new();

        private bool AtLineStart => Builder.Length == 0 || Builder[^1] == '\n';

        public void AppendText(string text)
        {
            var collapsed = CollapseWhitespace(text);
            if (AtLineStart || (Builder.Length > 0 && Builder[^1] == ' '))
            {
                collapsed = collapsed.TrimStart();
            }

            if (collapsed.Length == 0)
            {
                return;
            }

            if (_pendingBullet is not null)
            {
                if (!AtLineStart)
                {
                    Builder.Append('\n');
                }

                Builder.Append(_pendingBullet);
                _pendingBullet = null;
            }

            Builder.Append(collapsed);
        }

        public void AppendRaw(string text)
        {
            if (_pendingBullet is not null)
            {
                AppendText(text);
                return;
            }

            Builder.Append(text);
        }

        public void StartBullet(int listDepth)
        {
            NewLine();
            _pendingBullet = new string(' ', (listDepth - 1) * 2) + "- ";
        }

        public void NewLine()
        {
            if (!AtLineStart)
            {
                Builder.Append('\n');
            }
        }

        public void BlankLine()
        {
            if (Builder.Length == 0)
            {
                return;
            }

            NewLine();
            if (Builder.Length < 2 || Builder[^2] != '\n')
            {
                Builder.Append('\n');
            }
        }

        public void BlankLineUnlessInList(int listDepth)
        {
            if (listDepth == 0)
            {
                BlankLine();
            }
            else
            {
                NewLine();
            }
        }

        private static string CollapseWhitespace(string text)
        {
            var builder = new StringBuilder(text.Length);
            var space = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }

                if (char.IsControl(c))
                {
                    // 改行やタブ以外の制御文字は捨てる
                    continue;
                }

                if (space)
                {
                    builder.Append(' ');
                }

                space = false;
                builder.Append(c);
            }

            if (space)
            {
                builder.Append(' ');
            }

            return builder.ToString();
        }
    }
}
