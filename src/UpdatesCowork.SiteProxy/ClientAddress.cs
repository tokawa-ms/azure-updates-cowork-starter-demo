using System.Net;

namespace UpdatesCowork.SiteProxy;

/// <summary>レート制限に使うクライアントの IP アドレスを決める。</summary>
internal static class ClientAddress
{
    public const string Unknown = "unknown";

    public static string Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Container Apps のイングレスは X-Forwarded-For の末尾に接続元を足す。
        // 手前の値は利用者が自由に書けるので使わず、末尾の1つだけを信じる。
        var forwardedFor = context.Request.Headers["X-Forwarded-For"];
        if (forwardedFor.Count > 0 && forwardedFor[^1] is { } header)
        {
            var last = header[(header.LastIndexOf(',') + 1)..].Trim();
            if (IPAddress.TryParse(last, out var forwarded))
            {
                return forwarded.ToString();
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? Unknown;
    }
}
