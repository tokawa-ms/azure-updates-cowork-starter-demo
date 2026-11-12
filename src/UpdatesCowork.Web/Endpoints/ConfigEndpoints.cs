using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Configuration;

namespace UpdatesCowork.Web.Endpoints;

/// <summary>正常性確認、画面の設定、サインインの状態。</summary>
internal static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app, AuthMode authMode)
    {
        app.MapMethods("/healthz", [HttpMethods.Get, HttpMethods.Head], () => TypedResults.Text("ok", "text/plain"));

        app.MapGet("/api/config", (IOptions<WebOptions> webOptions, IOptions<AuthOptions> authOptions, HttpContext context) =>
        {
            var web = webOptions.Value;
            context.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new ClientConfig(
                authOptions.Value.Mode.ToString(),
                new ClientFeatures(web.Features.Approvals, web.Features.LearningMode)));
        });

        app.MapGet("/api/me", (HttpContext context, IOptions<AuthOptions> authOptions) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var mode = authOptions.Value.Mode;
            if (mode != AuthMode.EntraId)
            {
                return TypedResults.Ok(new MeResponse(mode.ToString(), false, null));
            }

            var user = context.User;
            var signedIn = user.Identity?.IsAuthenticated == true;
            return TypedResults.Ok(new MeResponse(mode.ToString(), signedIn, signedIn ? user.GetDisplayName() : null));
        });

        if (authMode == AuthMode.EntraId)
        {
            app.MapGet("/auth/signin", () => TypedResults.Challenge(
                new AuthenticationProperties { RedirectUri = "/" },
                [OpenIdConnectDefaults.AuthenticationScheme]));

            app.MapGet("/auth/signout", () => TypedResults.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
        }

        return app;
    }
}
