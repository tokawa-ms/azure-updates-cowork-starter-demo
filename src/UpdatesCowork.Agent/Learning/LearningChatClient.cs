using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UpdatesCowork.Shared.Telemetry;

namespace UpdatesCowork.Agent.Learning;

internal sealed class LearningChatClient(IChatClient innerClient, AgentTelemetry telemetry, string model)
    : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var activity = Start(options);
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
            RecordUsage(activity, response.Usage);
            return response;
        }
        catch (Exception ex)
        {
            MarkError(activity, ex, cancellationToken);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = Start(options);
        var completed = false;
        var totalUsage = new UsageDetails();
        await using var updates = base.GetStreamingResponseAsync(messages, options, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (await NextAsync(updates, activity, cancellationToken).ConfigureAwait(false))
            {
                foreach (var usage in updates.Current.Contents.OfType<UsageContent>())
                {
                    totalUsage.Add(usage.Details);
                    RecordUsage(activity, totalUsage);
                }
                yield return updates.Current;
            }
            completed = true;
        }
        finally
        {
            if (!completed && activity?.Status != ActivityStatusCode.Error)
            {
                activity?.SetTag(TelemetryNames.Attributes.Outcome, "cancelled");
            }
        }
    }

    private Activity? Start(ChatOptions? options) => telemetry.Source.StartActivity(
        TelemetryNames.Spans.ModelCall, ActivityKind.Client, Activity.Current?.Context ?? default, tags: new TagList
        {
            { TelemetryNames.Attributes.Model, options?.ModelId ?? model },
        });

    private static async ValueTask<bool> NextAsync(IAsyncEnumerator<ChatResponseUpdate> updates, Activity? activity, CancellationToken ct)
    {
        try
        {
            return await updates.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MarkError(activity, ex, ct);
            throw;
        }
    }

    private static void MarkError(Activity? activity, Exception error, CancellationToken ct)
    {
        if (error is OperationCanceledException && ct.IsCancellationRequested)
        {
            activity?.SetTag(TelemetryNames.Attributes.Outcome, "cancelled");
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(TelemetryNames.Attributes.ErrorType, error.GetType().Name);
        }
    }

    private static void RecordUsage(Activity? activity, UsageDetails? usage)
    {
        if (usage?.InputTokenCount is { } input)
        {
            activity?.SetTag(TelemetryNames.Attributes.InputTokens, input);
        }
        if (usage?.OutputTokenCount is { } output)
        {
            activity?.SetTag(TelemetryNames.Attributes.OutputTokens, output);
        }
    }
}
