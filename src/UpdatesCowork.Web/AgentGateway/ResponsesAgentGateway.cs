using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Web.Configuration;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>
/// Foundry の Hosted Agent (またはローカルのエージェント)を Responses プロトコルで呼ぶ(5.2節、6.4節)。
/// HttpClient は型付きクライアントで、タイムアウトは無制限にして取り消しは CancellationToken で行う。
/// </summary>
internal sealed partial class ResponsesAgentGateway(
    HttpClient httpClient,
    IOptions<AgentOptions> options,
    TimeProvider timeProvider,
    ILogger<ResponsesAgentGateway> logger) : IAgentGateway
{
    public const string ClientTraceIdHeader = "x-client-trace-id";
    public const string ClientThreadIdHeader = "x-client-thread-id";
    public const string UserIdentityHeader = "x-ms-user-identity";
    public const string VersionOverrideHeader = "x-agent-version-override";

    /// <summary>424 (セッションの準備中)のときに再試行するまでの待ち時間。テストでは 0 にする。</summary>
    internal TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public async Task<string> CreateConversationAsync(AgentCallContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var response = await SendAsync(context, "conversations", "{}"u8.ToArray(), acceptEventStream: false, cancellationToken)
            .ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() is { Length: > 0 } conversationId)
            {
                return conversationId;
            }
        }

        throw new AgentGatewayException(ChatErrorCodes.AgentError, "会話を作成できませんでした。");
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = AgentRequestBuilder.BuildResponsesBody(request);
        using var response = await SendAsync(request.Context, "responses", body, acceptEventStream: true, cancellationToken)
            .ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            var recorder = CreateRecorder(request.Context.ThreadId);
            try
            {
                await foreach (var evt in AgentSseReader.ReadAsync(stream, logger, cancellationToken).ConfigureAwait(false))
                {
                    if (recorder is not null)
                    {
                        await recorder.WriteAsync($"event: {evt.Type}\ndata: {evt.Data.GetRawText()}\n\n".AsMemory(), cancellationToken)
                            .ConfigureAwait(false);
                    }

                    yield return evt;
                }
            }
            finally
            {
                if (recorder is not null)
                {
                    await recorder.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    internal Uri BuildUri(string relativePath)
    {
        var value = options.Value;
        var baseUrl = value.BaseUrl.TrimEnd('/');
        var uri = $"{baseUrl}/{relativePath}";
        if (!string.IsNullOrWhiteSpace(value.ApiVersion))
        {
            uri += (uri.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "api-version=" + Uri.EscapeDataString(value.ApiVersion);
        }

        return new Uri(uri, UriKind.Absolute);
    }

    private async Task<HttpResponseMessage> SendAsync(
        AgentCallContext context,
        string relativePath,
        byte[] body,
        bool acceptEventStream,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = CreateRequest(context, relativePath, body, acceptEventStream);
            HttpResponseMessage response;
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                LogConnectionFailed(logger, relativePath, ex.GetType().Name);
                throw new AgentGatewayException(ChatErrorCodes.AgentError, "エージェントに接続できません。", innerException: ex);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var status = (int)response.StatusCode;
            if (status == 424 && attempt == 0)
            {
                // session_not_ready: セッションの準備を待って1回だけやり直す。
                LogRetrying(logger, relativePath);
                response.Dispose();
                await Task.Delay(RetryDelay, timeProvider, cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                LogFailed(logger, relativePath, (int)response.StatusCode);
                throw MapError(response);
            }
        }
    }

    private HttpRequestMessage CreateRequest(AgentCallContext context, string relativePath, byte[] body, bool acceptEventStream)
    {
        var value = options.Value;
        var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(relativePath))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(acceptEventStream ? "text/event-stream" : "application/json"));

        if (Activity.Current is { } activity)
        {
            request.Headers.TryAddWithoutValidation(ClientTraceIdHeader, activity.TraceId.ToHexString());
        }

        request.Headers.TryAddWithoutValidation(ClientThreadIdHeader, context.ThreadId);
        if (value.SendUserIdentity && !string.IsNullOrEmpty(context.UserKey))
        {
            request.Headers.TryAddWithoutValidation(UserIdentityHeader, context.UserKey);
        }

        if (!string.IsNullOrWhiteSpace(value.VersionOverride))
        {
            request.Headers.TryAddWithoutValidation(VersionOverrideHeader, value.VersionOverride.Trim());
        }

        return request;
    }

    private static AgentGatewayException MapError(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new AgentGatewayException(
                ChatErrorCodes.AgentForbidden,
                "エージェントを呼ぶ権限がありません。",
                status),
            HttpStatusCode.NotFound => new AgentGatewayException(
                ChatErrorCodes.AgentNotFound,
                "エージェントが見つかりません。",
                status),
            HttpStatusCode.TooManyRequests => new AgentGatewayException(
                ChatErrorCodes.RateLimited,
                "混み合っています。しばらく待ってからもう一度お試しください。",
                status,
                GetRetryAfter(response)),
            _ => new AgentGatewayException(
                ChatErrorCodes.AgentError,
                "エージェントでエラーが発生しました。",
                status),
        };
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is not { } retryAfter)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private StreamWriter? CreateRecorder(string threadId)
    {
        var directory = options.Value.RecordTo;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        Directory.CreateDirectory(directory);
        var stamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"{stamp}-{threadId}.sse");
        return new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { NewLine = "\n" };
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "エージェントに接続できませんでした: {Path} {ExceptionType}")]
    private static partial void LogConnectionFailed(ILogger logger, string path, string exceptionType);

    [LoggerMessage(Level = LogLevel.Information, Message = "エージェントのセッションの準備中(424)のため、再試行します: {Path}")]
    private static partial void LogRetrying(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "エージェントの呼び出しが失敗しました: {Path} {StatusCode}")]
    private static partial void LogFailed(ILogger logger, string path, int statusCode);

}
