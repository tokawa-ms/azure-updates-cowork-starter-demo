using Microsoft.Agents.AI;

namespace UpdatesCowork.Agent;

internal sealed class CurrentTimeContextProvider(TimeProvider time) : AIContextProvider
{
    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var now = TimeZoneInfo.ConvertTime(time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));
        return ValueTask.FromResult(new AIContext { Instructions = $"現在日時: {now:yyyy-MM-dd HH:mm} (日本時間)" });
    }
}
