using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>Responses の SSE を <see cref="AgentStreamEvent"/> の列にする(録画の再生とテストでも使う)。</summary>
internal static partial class AgentSseReader
{
    public static async IAsyncEnumerable<AgentStreamEvent> ReadAsync(
        Stream stream,
        ILogger? logger,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var parser = SseParser.Create(stream);
        await foreach (var item in parser.EnumerateAsync(cancellationToken).ConfigureAwait(false))
        {
            var evt = TryCreate(item, logger);
            if (evt is not null)
            {
                yield return evt;
            }
        }
    }

    private static AgentStreamEvent? TryCreate(SseItem<string> item, ILogger? logger)
    {
        if (string.IsNullOrWhiteSpace(item.Data) || item.Data == "[DONE]")
        {
            return null;
        }

        JsonElement data;
        try
        {
            using var document = JsonDocument.Parse(item.Data);
            data = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            if (logger is not null)
            {
                LogInvalidJson(logger, item.EventType);
            }

            return null;
        }

        // data の type を優先し、なければ event: 行の名前を使う。
        var type = data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("type", out var typeElement)
            && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;
        return new AgentStreamEvent(type ?? item.EventType, data);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "JSON として読めない SSE のイベントを捨てました: {EventType}")]
    private static partial void LogInvalidJson(ILogger logger, string eventType);
}
