using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using UpdatesCowork.Shared;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;
using UpdatesCowork.Web.AgentGateway;
using UpdatesCowork.Web.Configuration;
using UpdatesCowork.Web.Registry;
using UpdatesCowork.Web.Telemetry;

namespace UpdatesCowork.Web.Chat;

/// <summary>1回分の依頼の内容。<see cref="Approval"/> があるときは承認の返事だけを送る。</summary>
internal sealed record ChatTurnInput
{
    public required ChatThread Thread { get; init; }

    public string? Action { get; init; }

    public ValidatedPageContext? Page { get; init; }

    public string? Message { get; init; }

    public AgentApprovalResponse? Approval { get; init; }

    public WebLearningTrace? WebTrace { get; init; }
}

/// <summary>
/// <c>POST /api/chat</c> の流れ(5.2節の手順3〜10)をまとめる。エージェントのイベントを画面向けに変え、
/// 成果物は台帳に登録してから知らせ、15秒ごとの <c>ping</c> と全体のタイムアウトを扱う。
/// </summary>
internal sealed partial class ChatOrchestrator(
    IAgentGateway gateway,
    IChatRegistry registry,
    IOptions<AgentOptions> agentOptions,
    WebTelemetry telemetry,
    TimeProvider timeProvider,
    ILogger<ChatOrchestrator> logger)
{
    public const string CancelledOutcome = "cancelled";

    /// <summary>イベントがないときに <c>ping</c> を送る間隔。</summary>
    internal TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>画面に送る SSE の項目を順に返す。<paramref name="requestAborted"/> で接続が切れたらエージェントへの要求も取り消す。</summary>
    public async IAsyncEnumerable<SseItem<string>> RunAsync(
        ChatTurnInput input,
        [EnumeratorCancellation] CancellationToken requestAborted)
    {
        ArgumentNullException.ThrowIfNull(input);

        var channel = Channel.CreateUnbounded<ChatEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var producerCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        var producer = Task.Run(() => ProduceAsync(input, channel.Writer, producerCts.Token), CancellationToken.None);
        try
        {
            var reader = channel.Reader;
            Task<bool>? waitTask = null;
            while (true)
            {
                waitTask ??= reader.WaitToReadAsync(requestAborted).AsTask();
                bool hasData;
                bool timedOut;
                try
                {
                    hasData = await waitTask.WaitAsync(PingInterval, timeProvider, requestAborted).ConfigureAwait(false);
                    timedOut = false;
                }
                catch (TimeoutException)
                {
                    hasData = false;
                    timedOut = true;
                }

                if (timedOut)
                {
                    // 待っている WaitToReadAsync はそのまま使い続ける。
                    yield return ToSseItem(new PingEvent());
                    continue;
                }

                waitTask = null;
                if (!hasData)
                {
                    break;
                }

                while (reader.TryRead(out var chatEvent))
                {
                    yield return ToSseItem(chatEvent);
                }
            }
        }
        finally
        {
            await producerCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await producer.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 接続が切れたときの取り消しは正常な終わり方として扱う。
            }
        }
    }

    internal static SseItem<string> ToSseItem(ChatEvent chatEvent) =>
        new(JsonSerializer.Serialize(chatEvent, chatEvent.GetType(), AucJsonContext.Default), chatEvent.EventName);

    private async Task ProduceAsync(ChatTurnInput input, ChannelWriter<ChatEvent> writer, CancellationToken requestAborted)
    {
        var options = agentOptions.Value;
        var startTimestamp = timeProvider.GetTimestamp();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, options.TurnTimeoutSeconds)), timeProvider);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, timeoutCts.Token);
        var ct = linkedCts.Token;

        var thread = input.Thread;
        var webTrace = input.WebTrace ?? new WebLearningTrace();
        using var turnSpan = webTrace.Start(TelemetryNames.Spans.ChatTurn, cancellationToken: ct);
        var activity = turnSpan.Activity;
        activity.SetTag(TelemetryNames.Attributes.ThreadId, thread.ThreadId);
        activity.SetTag(TelemetryNames.Attributes.UserKey, thread.UserKey);
        activity.SetTag(TelemetryNames.Attributes.Action, input.Action);
        activity.SetTag(TelemetryNames.Attributes.PageUpdateId, input.Page?.UpdateId);
        var traceId = activity.TraceId.ToHexString();

        var outcome = TelemetryNames.Outcomes.Error;
        var eventCount = 0;
        double? ttftMs = null;
        ChatEventMapper? mapper = null;
        ChatEvent? terminal = null;
        webTrace.Attach(chatEvent =>
        {
            if (!writer.TryWrite(chatEvent)) throw new InvalidOperationException("Web の計測イベントを送信できませんでした。");
            eventCount++;
        });

        async Task WriteAsync(ChatEvent chatEvent)
        {
            // 終了通知は Web の最終計測と台帳更新の後に送る。
            if (chatEvent is DoneEvent or ErrorEvent)
            {
                if (terminal is not ErrorEvent) terminal = chatEvent;
                return;
            }
            await writer.WriteAsync(chatEvent, ct).ConfigureAwait(false);
            eventCount++;
        }

        try
        {
            var callContext = new AgentCallContext(thread.ThreadId, thread.UserKey);
            var useConversations = options.ConversationMode == ConversationMode.Conversations;
            if (useConversations && string.IsNullOrEmpty(thread.ConversationId))
            {
                string conversationId;
                using (var conversationSpan = webTrace.Start(TelemetryNames.Spans.AgentConversationCreate, ActivityKind.Client, ct))
                {
                    conversationId = await gateway.CreateConversationAsync(callContext, ct).ConfigureAwait(false);
                    conversationSpan.Activity.SetTag(TelemetryNames.Attributes.ConversationId, conversationId);
                    conversationSpan.Complete();
                }

                thread = thread with { ConversationId = conversationId };
                await registry.UpdateThreadAsync(thread, ct).ConfigureAwait(false);
            }

            activity?.SetTag(TelemetryNames.Attributes.ConversationId, thread.ConversationId);

            var request = new AgentTurnRequest
            {
                Context = callContext,
                ConversationId = useConversations ? thread.ConversationId : null,
                PreviousResponseId = useConversations ? null : thread.LastResponseId,
                DeveloperText = input.Approval is null ? AgentRequestBuilder.BuildPageContextText(input.Page) : null,
                UserText = input.Approval is null ? input.Message : null,
                Approval = input.Approval,
            };

            mapper = new ChatEventMapper(thread.ThreadId, thread.ConversationId, traceId, logger);
            using (var invokeSpan = webTrace.Start(TelemetryNames.Spans.WebAgentInvoke, ActivityKind.Client, ct))
            {
                WebLearningTrace.Span? relaySpan = null;
                try
                {
                    await foreach (var agentEvent in gateway.StreamAsync(request, ct).ConfigureAwait(false))
                    {
                        relaySpan ??= webTrace.Start(TelemetryNames.Spans.WebSseRelay, cancellationToken: ct);
                        foreach (var chatEvent in mapper.Map(agentEvent))
                        {
                            if (chatEvent is TextDeltaEvent && ttftMs is null)
                            {
                                ttftMs = timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
                                telemetry.RecordTtft(input.Action, ttftMs.Value);
                            }
                            await WriteAsync(chatEvent).ConfigureAwait(false);
                            if (chatEvent is ToolCompletedEvent)
                            {
                                // 成果物は台帳に登録してから画面に知らせる。
                                while (mapper.TryDequeueArtifact(out var artifact))
                                {
                                    using (var registerSpan = webTrace.Start(TelemetryNames.Spans.WebArtifactRegister, cancellationToken: ct))
                                    {
                                        if (artifact.SessionId is { Length: > 0 } sessionId && thread.SessionId != sessionId)
                                        {
                                            thread = thread with { SessionId = sessionId };
                                            await registry.UpdateThreadAsync(thread, ct).ConfigureAwait(false);
                                        }
                                        await registry.AddArtifactAsync(thread.UserKey, ToRecord(artifact, thread.ThreadId), ct).ConfigureAwait(false);
                                        registerSpan.Complete();
                                    }
                                    await WriteAsync(new ArtifactCreatedEvent(
                                        artifact.ArtifactId, artifact.FileName, artifact.Kind, artifact.SizeBytes,
                                        $"/api/artifacts/{artifact.ArtifactId}")).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    relaySpan?.Complete();
                    if (mapper.DoneStatus is not null && mapper.ErrorCode is null) invokeSpan.Complete();
                }
                finally
                {
                    relaySpan?.Dispose();
                }
            }

            activity?.SetTag(TelemetryNames.Attributes.ResponseId, mapper.ResponseId);
            if (mapper.DoneStatus is { } status)
            {
                outcome = mapper.ErrorCode is not null ? TelemetryNames.Outcomes.Error
                    : status == "completed" ? TelemetryNames.Outcomes.Completed : TelemetryNames.Outcomes.Incomplete;
                thread = thread with
                {
                    LastResponseId = mapper.ResponseId ?? thread.LastResponseId,
                    ConversationId = mapper.ConversationId ?? thread.ConversationId,
                };

                // 画面との接続が切れても、最後の応答 ID は保存しておく。
                await registry.UpdateThreadAsync(thread, CancellationToken.None).ConfigureAwait(false);
            }
            else if (mapper.ErrorCode is null)
            {
                terminal = new ErrorEvent(ChatErrorCodes.AgentError, "応答が途中で終わりました。もう一度お試しください。", traceId);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, mapper.ErrorCode);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !requestAborted.IsCancellationRequested)
        {
            outcome = TelemetryNames.Outcomes.Timeout;
            activity?.SetStatus(ActivityStatusCode.Error, ChatErrorCodes.Timeout);
            LogTimeout(logger, thread.ThreadId);
            terminal = new ErrorEvent(ChatErrorCodes.Timeout, "時間内に応答が終わりませんでした。依頼を短くして、もう一度お試しください。", traceId);
        }
        catch (OperationCanceledException) when (requestAborted.IsCancellationRequested)
        {
            outcome = CancelledOutcome;
        }
        catch (AgentGatewayException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Code);
            LogGatewayError(logger, ex.Code, ex.StatusCode ?? 0);
            terminal = new ErrorEvent(ex.Code, ex.Message, traceId);
        }
#pragma warning disable CA1031 // 予期しない例外でも、画面には短いエラーを返してストリームを閉じる必要があるため。
        catch (Exception ex)
#pragma warning restore CA1031
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            LogUnexpectedError(logger, ex, thread.ThreadId);
            terminal = new ErrorEvent(ChatErrorCodes.AgentError, "エラーが発生しました。もう一度お試しください。", traceId);
        }
        finally
        {
            var durationMs = timeProvider.GetElapsedTime(startTimestamp).TotalMilliseconds;
            activity?.SetTag(TelemetryNames.Attributes.Events, eventCount + 2 + (terminal is not null && !requestAborted.IsCancellationRequested ? 1 : 0));
            activity?.SetTag(TelemetryNames.Attributes.Outcome, outcome);
            if (ttftMs is { } ttft)
            {
                activity?.SetTag(TelemetryNames.Attributes.TtftMs, ttft);
            }

            if (mapper is { ReasoningCount: > 0 })
            {
                LogReasoningCount(logger, mapper.ReasoningCount);
            }

            telemetry.RecordTurn(input.Action, outcome, durationMs);
            turnSpan.Finish(outcome == CancelledOutcome ? "cancelled"
                : terminal is ErrorEvent || outcome is TelemetryNames.Outcomes.Error or TelemetryNames.Outcomes.Timeout ? "failed" : "completed");
            webTrace.Finish();
            if (terminal is not null && !requestAborted.IsCancellationRequested)
            {
                if (!writer.TryWrite(terminal)) LogTerminalWriteFailed(logger);
            }
            writer.TryComplete();
        }
    }

    private ArtifactRecord ToRecord(ArtifactInfo artifact, string threadId) => new()
    {
        ArtifactId = artifact.ArtifactId,
        ThreadId = threadId,
        FileName = artifact.FileName,
        ContentType = artifact.ContentType,
        Kind = artifact.Kind,
        SizeBytes = artifact.SizeBytes,
        Storage = artifact.Storage,
        Location = artifact.Location,
        SessionId = artifact.SessionId,
        CreatedUtc = timeProvider.GetUtcNow(),
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "依頼が時間内に終わりませんでした: {ThreadId}")]
    private static partial void LogTimeout(ILogger logger, string threadId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "エージェントの呼び出しが失敗しました: {Code} {StatusCode}")]
    private static partial void LogGatewayError(ILogger logger, string code, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "依頼の処理中に予期しないエラーが発生しました: {ThreadId}")]
    private static partial void LogUnexpectedError(ILogger logger, Exception exception, string threadId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "推論の要約(reasoning)の件数: {Count}")]
    private static partial void LogReasoningCount(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "応答の終了イベントを送信できませんでした。")]
    private static partial void LogTerminalWriteFailed(ILogger logger);
}
