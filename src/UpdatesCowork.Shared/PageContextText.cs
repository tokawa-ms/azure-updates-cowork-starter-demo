using System.Net;
using System.Text;
using UpdatesCowork.Shared.Contracts;

namespace UpdatesCowork.Shared;

/// <summary>実際のチャットと固定データ評価で共通の developer メッセージ。</summary>
public static class PageContextText
{
    public static string? Format(PageContext? page)
    {
        if (page is null) return null;
        var text = new StringBuilder("<page_context>\n<untrusted_content>\n");
        Append(text, "upstream_url", page.UpstreamUrl);
        Append(text, "update_id", page.UpdateId);
        Append(text, "title", page.Title);
        Append(text, "locale", page.Lang);
        return text.Append("</untrusted_content>\n</page_context>").ToString();
    }

    private static void Append(StringBuilder text, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            text.Append(name).Append(": ").Append(WebUtility.HtmlEncode(value)).Append('\n');
        }
    }
}
