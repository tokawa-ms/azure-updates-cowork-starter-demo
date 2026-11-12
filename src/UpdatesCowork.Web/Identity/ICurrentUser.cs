using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace UpdatesCowork.Web.Identity;

/// <summary>今の要求の利用者(5.2節「利用者の識別」)。</summary>
internal interface ICurrentUser
{
    /// <summary>利用者キー(ハッシュ値)。識別できないときは <c>null</c>。</summary>
    string? UserKey { get; }
}

/// <summary>ミドルウェアが <see cref="HttpContext.Items"/> に入れた利用者キーを返す。</summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserKey => accessor.HttpContext is { } context ? UserKeys.Get(context) : null;
}

/// <summary>利用者キーの計算と受け渡し。Cookie の値やメールアドレスはどこにも残さない。</summary>
internal static class UserKeys
{
    private const string ItemKey = "auc.user_key";

    public static string? Get(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
    }

    public static void Set(HttpContext context, string userKey)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[ItemKey] = userKey;
    }

    /// <summary>匿名の Cookie の値から利用者キーを作る(SHA-256 の先頭32文字)。</summary>
    public static string FromAnonymousToken(string token) => Hash(token);

    /// <summary>Entra ID の <c>tid</c> と <c>oid</c> を連結した値から利用者キーを作る。</summary>
    public static string FromEntraIds(string tenantId, string objectId) => Hash(tenantId + objectId);

    /// <summary>32 バイトの乱数を Base64url にした匿名トークンを作る。</summary>
    public static string NewAnonymousToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.EncodeToString(bytes);
    }

    /// <summary>匿名トークンの形(Base64url で 43 文字)かを確かめる。</summary>
    public static bool IsValidAnonymousToken(string? token)
    {
        if (token is null || token.Length != 43)
        {
            return false;
        }

        foreach (var c in token)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
