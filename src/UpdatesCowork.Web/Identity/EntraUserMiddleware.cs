using Microsoft.Identity.Web;

namespace UpdatesCowork.Web.Identity;

/// <summary>
/// Entra ID モード(<c>Auth:Mode=EntraId</c>)の利用者識別。サインイン済みなら <c>tid</c> と <c>oid</c> から
/// 利用者キーを作る。サインインしていない要求は、画面の表示と設定の取得だけを許し、ほかの API は 401 にする。
/// </summary>
internal sealed class EntraUserMiddleware(RequestDelegate next)
{
    private static readonly string[] AnonymousApiPaths = ["/api/config", "/api/me"];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && user.GetTenantId() is { Length: > 0 } tenantId
            && user.GetObjectId() is { Length: > 0 } objectId)
        {
            UserKeys.Set(context, UserKeys.FromEntraIds(tenantId, objectId));
        }
        else if (RequiresSignIn(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new Endpoints.ApiError("unauthorized", "サインインしてください。"),
                Endpoints.WebJsonContext.Default.ApiError).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool RequiresSignIn(PathString path)
    {
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var allowed in AnonymousApiPaths)
        {
            if (path.Equals(allowed, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
