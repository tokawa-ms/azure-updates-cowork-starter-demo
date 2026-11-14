using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UpdatesCowork.Shared.Contracts;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent;

internal sealed class ToolCallMiddleware(AgentTelemetry telemetry, ILogger<ToolCallMiddleware> logger)
{
    internal const int MaximumCalls = 12;
    private readonly AsyncLocal<CallBudget?> _budget = new();

    internal async Task RunAsync(IEnumerable<ChatMessage> messages, AgentSession? session, AgentRunOptions? options,
        Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, CancellationToken, Task> next, CancellationToken cancellationToken)
    {
        var previous = _budget.Value;
        _budget.Value = new CallBudget();
        try
        {
            await next(messages, session, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _budget.Value = previous;
        }
    }

    internal async ValueTask<object?> InvokeAsync(AIAgent agent, FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next, CancellationToken cancellationToken)
    {
        var budget = _budget.Value ?? throw new InvalidOperationException("呼び出し回数の管理が開始されていません。");
        using var activity = telemetry.Source.StartActivity(TelemetryNames.Spans.ToolExecute, ActivityKind.Internal,
            Activity.Current?.Context ?? default, tags: new TagList
        {
            { TelemetryNames.Attributes.ToolName, context.Function.Name },
            { TelemetryNames.Attributes.ToolCallId, context.CallContent?.CallId },
        });
        var start = Stopwatch.GetTimestamp();
        var outcome = TelemetryNames.Outcomes.Error;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!budget.TryAcquire())
            {
                AgentLog.LimitReached(logger);
                return Error("ツール呼び出しの上限に達しました。ここまでの結果で答えてください。");
            }

            var result = await next(context, cancellationToken).ConfigureAwait(false);
            var failed = result is ArtifactResult { Ok: false }
                || (result is JsonElement { ValueKind: JsonValueKind.Object } json
                    && json.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False);
            outcome = failed ? TelemetryNames.Outcomes.Error : TelemetryNames.Outcomes.Ok;
            if (failed)
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error);
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        catch (Exception ex)
        {
            // ツール境界では例外の本文をモデルとログに渡さない。
            AgentLog.ToolFailed(logger, context.Function.Name, ex.GetType().Name);
            Activity.Current?.SetTag(TelemetryNames.Attributes.ErrorType, ex.GetType().FullName);
            Activity.Current?.SetStatus(ActivityStatusCode.Error);
            return Error("internal_error");
        }
        finally
        {
            activity?.SetTag(TelemetryNames.Attributes.Outcome, outcome);
            if (outcome == TelemetryNames.Outcomes.Error)
            {
                activity?.SetStatus(ActivityStatusCode.Error);
            }
            var tags = new TagList
            {
                { TelemetryNames.Dimensions.Tool, context.Function.Name },
                { TelemetryNames.Dimensions.Outcome, outcome },
            };
            telemetry.ToolCalls.Add(1, tags);
            telemetry.ToolDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
        }
    }

    internal static JsonElement Error(string message) =>
        JsonSerializer.SerializeToElement(new ToolError(false, message), AgentJsonContext.Default.ToolError);

    internal sealed class CallBudget
    {
        private int _calls;
        internal bool TryAcquire() => Interlocked.Increment(ref _calls) <= MaximumCalls;
    }
}
