using System.Net.Http.Headers;
using Azure.Core;
using Microsoft.Extensions.Options;
using UpdatesCowork.Web.Configuration;

namespace UpdatesCowork.Web.AgentGateway;

/// <summary>
/// <c>Agent:Mode=Foundry</c> のときだけ、<see cref="TokenCredential"/> でスコープ
/// <c>https://ai.azure.com/.default</c> のトークンを付ける。ローカルのエージェントには付けない。
/// </summary>
internal sealed class BearerTokenHandler(TokenCredential credential, IOptions<AgentOptions> options, TimeProvider timeProvider)
    : DelegatingHandler
{
    public const string Scope = "https://ai.azure.com/.default";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private AccessToken? _cached;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (options.Value.Mode == AgentMode.Foundry)
        {
            var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lock.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (IsFresh(_cached))
        {
            return _cached!.Value.Token;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsFresh(_cached))
            {
                _cached = await credential.GetTokenAsync(new TokenRequestContext([Scope]), cancellationToken).ConfigureAwait(false);
            }

            return _cached!.Value.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool IsFresh(AccessToken? token) =>
        token is { } value && value.ExpiresOn - RefreshMargin > timeProvider.GetUtcNow();
}
