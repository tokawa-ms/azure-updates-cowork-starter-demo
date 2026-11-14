using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Learning;

internal sealed partial class LearningResponseHandler(
    ResponseHandler inner, LearningTraceCollector collector, AgentTelemetry telemetry, ILogger<LearningResponseHandler> logger) : ResponseHandler
{
    public override async IAsyncEnumerable<ResponseStreamEvent> CreateAsync(CreateResponse request, ResponseContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var session = collector.CreateSession();
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = Task.Run(() => ProduceAsync(session, request, context, producerCts.Token), CancellationToken.None);
        var created = false;
        BinaryData? responseSnapshot = null;
        long sequence = 0;
        var deferred = new List<TraceSpanEvent>();
        ResponseStreamEvent? terminal = null;
        try
        {
            await foreach (var item in session.Events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (item.Response is { } response)
                {
                    var type = response.EventType.ToString();
                    if (type is "response.completed" or "response.incomplete" or "response.failed")
                    {
                        terminal = response;
                        continue;
                    }
                    if (!created && type == "response.created")
                    {
                        using var document = JsonDocument.Parse(ModelReaderWriter.Write(response));
                        responseSnapshot = BinaryData.FromString(document.RootElement.GetProperty("response").GetRawText());
                    }
                    response.SequenceNumber = sequence++;
                    yield return response;
                    if (!created && type == "response.created")
                    {
                        created = true;
                        yield return ToEvent(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, sequence++, context.ResponseId,
                            Status: new TraceStatusEvent("started", 0)), responseSnapshot!);
                        foreach (var span in deferred)
                        {
                            yield return ToEvent(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, sequence++, context.ResponseId, span), responseSnapshot!);
                        }
                        deferred.Clear();
                    }
                }
                else if (item.Span is { } span)
                {
                    if (created)
                    {
                        yield return ToEvent(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, sequence++, context.ResponseId, span), responseSnapshot!);
                    }
                    else
                    {
                        deferred.Add(span);
                    }
                }
            }
            if (created)
            {
                if (session.DroppedEvents > 0)
                {
                    LogDropped(logger, session.DroppedEvents);
                }
                yield return ToEvent(new AgentTraceEnvelope(AgentTraceEnvelope.EventType, sequence++, context.ResponseId,
                    Status: new TraceStatusEvent(session.DroppedEvents == 0 ? "complete" : "limited", session.DroppedEvents)), responseSnapshot!);
            }
            if (terminal is not null)
            {
                terminal.SequenceNumber = sequence;
                yield return terminal;
            }
        }
        finally
        {
            try
            {
                await producerCts.CancelAsync().ConfigureAwait(false);
                await producer.ConfigureAwait(false);
            }
            finally
            {
                collector.Remove(session);
            }
        }
    }

    private async Task ProduceAsync(LearningTraceSession session, CreateResponse request, ResponseContext context, CancellationToken ct)
    {
        Exception? error = null;
        using var scope = LearningTraceCollector.Enter(session);
        using (var activity = telemetry.Source.StartActivity(TelemetryNames.Spans.AgentTurn))
        {
            try
            {
                await foreach (var response in inner.CreateAsync(request, context, ct).ConfigureAwait(false))
                {
                    if (response.EventType.ToString() == "response.failed")
                    {
                        activity?.SetStatus(ActivityStatusCode.Error);
                    }
                    await session.Events.Writer.WriteAsync(new LearningTraceItem(Response: response), ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                error = ex;
                if (ex is OperationCanceledException && ct.IsCancellationRequested)
                {
                    activity?.SetTag(TelemetryNames.Attributes.Outcome, "cancelled");
                }
                else
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                }
            }
        }
        session.Events.Writer.TryComplete(error);
    }

    // 固定 SDK の SSE シリアライザーは未知イベントで再帰するため、既知の進捗イベントの追加 JSON 領域を使う。
    internal static ResponseStreamEvent ToEvent(AgentTraceEnvelope envelope, BinaryData responseSnapshot)
    {
        using var buffer = new MemoryStream();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("type", AgentTraceEnvelope.CarrierEventType);
        writer.WriteNumber("sequence_number", envelope.SequenceNumber);
        writer.WritePropertyName("response");
        writer.WriteRawValue(responseSnapshot.ToMemory().Span);
        writer.WritePropertyName(AgentTraceEnvelope.MetadataProperty);
        JsonSerializer.Serialize(writer, envelope, AucJsonContext.Default.AgentTraceEnvelope);
        writer.WriteEndObject();
        writer.Flush();
        return ModelReaderWriter.Read<ResponseStreamEvent>(BinaryData.FromBytes(buffer.ToArray()))
            ?? throw new InvalidOperationException("学習トレースをイベントに変換できませんでした。");
    }

    [LoggerMessage(20, LogLevel.Warning, "学習トレースの上限により {Count} 件のイベントを省略しました。")]
    private static partial void LogDropped(ILogger logger, int count);
}
