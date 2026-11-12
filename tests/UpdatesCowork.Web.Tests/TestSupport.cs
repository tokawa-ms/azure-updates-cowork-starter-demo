using System.Net;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using UpdatesCowork.Web.AgentGateway;

namespace UpdatesCowork.Web.Tests;

/// <summary>tests/recordings にある Responses の SSE の録画を読む。</summary>
internal static class Recordings
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "recordings", name);

    public static async Task<List<AgentStreamEvent>> LoadAsync(string name)
    {
        var stream = File.OpenRead(PathOf(name));
        await using (stream)
        {
            var events = new List<AgentStreamEvent>();
            await foreach (var evt in AgentSseReader.ReadAsync(stream, null, CancellationToken.None))
            {
                events.Add(evt);
            }

            return events;
        }
    }
}

/// <summary>画面向けの SSE の1件。</summary>
internal sealed record ClientEvent(string Name, JsonElement Data);

internal static class ClientSse
{
    public static async Task<List<ClientEvent>> ParseAsync(string body)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
        var events = new List<ClientEvent>();
        await foreach (var item in SseParser.Create(stream).EnumerateAsync())
        {
            using var document = JsonDocument.Parse(item.Data);
            events.Add(new ClientEvent(item.EventType, document.RootElement.Clone()));
        }

        return events;
    }

    public static List<ClientEvent> Parse(IEnumerable<SseItem<string>> items) =>
        [.. items.Select(item =>
        {
            using var document = JsonDocument.Parse(item.Data);
            return new ClientEvent(item.EventType, document.RootElement.Clone());
        })];
}

/// <summary>送られた要求を記録し、決めた応答を返す HttpMessageHandler。</summary>
internal sealed class StubHttpHandler(Func<int, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, headers, request.Content?.Headers.ContentType?.MediaType, body));
        return respond(Requests.Count - 1);
    }

    public static HttpResponseMessage EventStream(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
    };

    public static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };
}

internal sealed record CapturedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    string Body);

/// <summary>録画を再生するテスト用のエージェント。受け取った要求を記録する。</summary>
internal sealed class RecordingAgentGateway(string recordingName) : IAgentGateway
{
    public string RecordingName { get; set; } = recordingName;

    /// <summary>設定すると、イベントの代わりにこの時間だけ待つ(タイムアウトの確認用)。</summary>
    public TimeSpan? Hang { get; set; }

    public List<AgentTurnRequest> Requests { get; } = [];

    public int ConversationsCreated { get; private set; }

    public Task<string> CreateConversationAsync(AgentCallContext context, CancellationToken cancellationToken)
    {
        ConversationsCreated++;
        return Task.FromResult($"conv_test_{ConversationsCreated:D4}");
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }

        if (Hang is { } hang)
        {
            await Task.Delay(hang, cancellationToken);
        }

        foreach (var evt in await Recordings.LoadAsync(RecordingName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return evt;
        }
    }
}
