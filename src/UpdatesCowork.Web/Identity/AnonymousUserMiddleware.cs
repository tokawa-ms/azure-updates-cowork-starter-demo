namespace UpdatesCowork.Web.Identity;

/// <summary>
/// 匿名モードの利用者識別。初回アクセスで <c>auc_uid</c> Cookie に 32 バイトの乱数を入れ、
/// その SHA-256 から利用者キーを作る。Cookie の値はログにもスパンにも残さない。
/// </summary>
internal sealed class AnonymousUserMiddleware(RequestDelegate next)
{
    public const string CookieName = "auc_uid";

    public static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(30);

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 正常性確認には Cookie を発行しない。
        if (context.Request.Path.StartsWithSegments("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        var token = context.Request.Cookies[CookieName];
        if (!UserKeys.IsValidAnonymousToken(token))
        {
            token = UserKeys.NewAnonymousToken();
            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = CookieLifetime,
                Path = "/",
                IsEssential = true,
            });
        }

        UserKeys.Set(context, UserKeys.FromAnonymousToken(token!));
        return next(context);
    }
}
