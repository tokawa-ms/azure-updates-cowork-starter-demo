using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using UpdatesCowork.Web.Configuration;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>
/// <c>Agent:Mode=Fake</c> のときに使う。録画した Responses の SSE をそのまま再生する。
/// エージェントもモデルも使わずに画面の動きを確かめるためのもの。
/// </summary>
internal sealed class FakeAgentGateway(
    IOptions<AgentOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider,
    ILogger<FakeAgentGateway> logger) : IAgentGateway
{
    public Task<string> CreateConversationAsync(AgentCallContext context, CancellationToken cancellationToken) =>
        Task.FromResult("conv_fake_" + Guid.NewGuid().ToString("N"));

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        AgentTurnRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var value = options.Value;
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, value.FakeRecording));
        var delay = TimeSpan.FromMilliseconds(Math.Max(0, value.FakeDelayMilliseconds));

        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var evt in AgentSseReader.ReadAsync(stream, logger, cancellationToken).ConfigureAwait(false))
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
                }

                yield return evt;
            }
        }
    }
}
