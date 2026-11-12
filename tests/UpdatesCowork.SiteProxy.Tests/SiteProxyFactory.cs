using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace UpdatesCowork.SiteProxy.Tests;

/// <summary>上流に届いた要求の記録。</summary>
internal sealed record UpstreamRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string[]> Headers)
{
    public bool HasHeader(string name) => Headers.ContainsKey(name);
}

/// <summary>ネットワークに出ずに、上流の応答を返す偽物。</summary>
internal sealed class FakeUpstream : HttpMessageHandler
{
    private readonly ConcurrentQueue<UpstreamRequest> _requests = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };

    public IReadOnlyList<UpstreamRequest> Requests => [.. _requests];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in request.Headers)
        {
            headers[name] = [.. values];
        }

        if (request.Content is not null)
        {
            foreach (var (name, values) in request.Content.Headers)
            {
                headers[name] = [.. values];
            }
        }

        _requests.Enqueue(new UpstreamRequest(request.Method, request.RequestUri!, headers));
        var response = Respond(request);
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>YARP が上流へ送るときの HTTP クライアントを偽物に差し替える。</summary>
internal sealed class FakeForwarderHttpClientFactory(HttpMessageHandler handler) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(handler, disposeHandler: false);
}

/// <summary>siteproxy をテスト用に起動する。上流への通信はすべて <see cref="FakeUpstream"/> に向く。</summary>
internal sealed class SiteProxyFactory(IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    public FakeUpstream Upstream { get; } = new();

    public HttpClient CreateProxyClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost:7102") });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Logging:LogLevel:Default", "Warning");
        builder.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", string.Empty);
        builder.UseSetting("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty);
        if (settings is not null)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        }

        builder.ConfigureTestServices(services =>
            services.AddSingleton<IForwarderHttpClientFactory>(new FakeForwarderHttpClientFactory(Upstream)));
    }
}
