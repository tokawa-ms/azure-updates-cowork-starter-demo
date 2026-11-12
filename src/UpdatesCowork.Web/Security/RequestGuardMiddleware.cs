using Microsoft.Net.Http.Headers;
using UpdatesCowork.Web.Endpoints;

namespace UpdatesCowork.Web.Security;

/// <summary>
/// 状態を変える API (<c>/api/</c> への POST など)に、<c>Content-Type: application/json</c> と
/// <c>X-AUC-Request: 1</c> を必須にする。別のサイトからのフォーム送信を受け付けないため。
/// </summary>
internal sealed class RequestGuardMiddleware(RequestDelegate next)
{
    public const string RequestHeaderName = "X-AUC-Request";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var request = context.Request;
        if (!IsUnsafeMethod(request.Method) || !request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return next(context);
        }

        if (!IsJsonContentType(request.ContentType) || !string.Equals(request.Headers[RequestHeaderName], "1", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return context.Response.WriteAsJsonAsync(
                new ApiError("invalid_request", "要求の形式が正しくありません。"),
                WebJsonContext.Default.ApiError);
        }

        return next(context);
    }

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool IsJsonContentType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var mediaType)
        && mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase);
}
