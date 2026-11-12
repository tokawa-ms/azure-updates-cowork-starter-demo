namespace UpdatesCowork.SiteProxy.Transforms;

/// <summary><c>Set-Cookie</c> から <c>Domain=</c> 属性を外し、Cookie を siteproxy のホストに結びつける。</summary>
internal static class SetCookieRewriter
{
    public static string RemoveDomain(string setCookie)
    {
        ArgumentNullException.ThrowIfNull(setCookie);

        var parts = setCookie.Split(';');
        if (parts.Length <= 1)
        {
            return setCookie;
        }

        // 先頭は Cookie の名前と値なので、名前が Domain でも残す。
        var kept = new List<string>(parts.Length) { parts[0] };
        for (var i = 1; i < parts.Length; i++)
        {
            if (!IsDomainAttribute(parts[i]))
            {
                kept.Add(parts[i]);
            }
        }

        return kept.Count == parts.Length ? setCookie : string.Join(';', kept);
    }

    private static bool IsDomainAttribute(string attribute)
    {
        var span = attribute.AsSpan().Trim();
        var eq = span.IndexOf('=');
        var name = (eq >= 0 ? span[..eq] : span).Trim();
        return name.Equals("Domain", StringComparison.OrdinalIgnoreCase);
    }
}
