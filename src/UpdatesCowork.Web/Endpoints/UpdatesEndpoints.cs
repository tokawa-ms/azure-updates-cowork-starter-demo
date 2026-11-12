using Microsoft.AspNetCore.Http.HttpResults;
using UpdatesCowork.AzureUpdates;

namespace UpdatesCowork.Web.Endpoints;

internal sealed record UpdateSummary(
    string Id, string Title, string? Status, IReadOnlyList<string> Products,
    DateTimeOffset? Modified, string Url, string? Locale);

internal sealed record UpdateDetail(
    UpdateSummary Update, string Html, IReadOnlyList<string> Tags,
    string? GeneralAvailabilityDate, string? PreviewAvailabilityDate);

internal sealed record UpdateListResponse(IReadOnlyList<UpdateSummary> Items, int Scanned, int Limit);

internal static class UpdatesEndpoints
{
    public static IEndpointRouteBuilder MapUpdatesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/updates", SearchAsync);
        app.MapGet("/api/updates/{id}", GetAsync);
        return app;
    }

    private static async Task<IResult> SearchAsync(
        string? keyword, string? product, string? status, string? modifiedSince,
        IAzureUpdatesClient updates, HttpContext context, CancellationToken cancellationToken)
    {
        DateOnly? since = null;
        if (!string.IsNullOrEmpty(modifiedSince))
        {
            if (!AzureUpdateQueryRules.TryParseDate(modifiedSince, out var date))
            {
                return Invalid("日付は YYYY-MM-DD の形式で指定してください。");
            }

            since = date;
        }

        var query = new AzureUpdateQuery
        {
            Keyword = keyword,
            Product = product,
            Status = AzureUpdateQueryRules.NormalizeText(status),
            ModifiedSince = since,
            Top = AzureUpdateQueryRules.MaxTop,
        };
        var errors = AzureUpdateQueryRules.Validate(query);
        if (errors.Count > 0)
        {
            return Invalid(string.Join(" ", errors));
        }

        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var result = await updates.SearchAsync(query, cancellationToken).ConfigureAwait(false);
            return TypedResults.Ok(new UpdateListResponse(
                result.Items.Select(Summarize).ToArray(), result.Scanned, query.Top));
        }
        catch (AzureUpdatesUnavailableException)
        {
            // 取得クライアントが詳細を記録済み。上流の応答本文はブラウザーに返さない。
            return Unavailable();
        }
    }

    private static async Task<IResult> GetAsync(
        string id, IAzureUpdatesClient updates, HttpContext context, CancellationToken cancellationToken)
    {
        if (!AzureUpdateQueryRules.IsValidId(id))
        {
            return Invalid("更新 ID の形式が正しくありません。");
        }

        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var update = await updates.GetAsync(id, cancellationToken).ConfigureAwait(false);
            return update is null
                ? TypedResults.NotFound(new ApiError("update_not_found", "更新情報が見つかりませんでした。"))
                : TypedResults.Ok(new UpdateDetail(Summarize(update), update.Description ?? string.Empty,
                    update.Tags ?? [], update.GeneralAvailabilityDate, update.PreviewAvailabilityDate));
        }
        catch (AzureUpdatesUnavailableException)
        {
            return Unavailable();
        }
    }

    private static UpdateSummary Summarize(AzureUpdate update) =>
        new(update.Id, update.Title ?? "タイトルなし", update.Status, update.Products ?? [],
            update.Modified, "https://azure.microsoft.com/updates?id=" + Uri.EscapeDataString(update.Id), update.Locale);

    private static BadRequest<ApiError> Invalid(string message) => TypedResults.BadRequest(new ApiError("invalid_request", message));

    private static JsonHttpResult<ApiError> Unavailable() => TypedResults.Json(
        new ApiError("updates_unavailable", "更新情報を取得できませんでした。時間をおいて再試行してください。"),
        WebJsonContext.Default.ApiError, statusCode: StatusCodes.Status503ServiceUnavailable);
}
