using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace UpdatesCowork.AzureUpdates;

/// <summary>データ API のクライアントを DI に登録する。</summary>
public static class AzureUpdatesServiceCollectionExtensions
{
    /// <summary>
    /// <see cref="IAzureUpdatesClient"/> を登録する。<c>AZURE_UPDATES_FIXTURES_DIR</c> があれば固定データ版、なければ HTTP 版。
    /// </summary>
    public static IServiceCollection AddAzureUpdatesClient(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddAzureUpdatesClient(AzureUpdatesOptions.FromConfiguration(configuration));
    }

    /// <summary><see cref="IAzureUpdatesClient"/> を、組み立て済みの設定で登録する。</summary>
    public static IServiceCollection AddAzureUpdatesClient(this IServiceCollection services, AzureUpdatesOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);

        if (options.FixturesDirectory is { } directory)
        {
            services.AddSingleton<IAzureUpdatesClient>(sp => new FixtureAzureUpdatesClient(
                directory,
                sp.GetService<ILoggerFactory>()?.CreateLogger<FixtureAzureUpdatesClient>()));
            return services;
        }

        services.AddMemoryCache();
        services
            .AddHttpClient<IAzureUpdatesClient, AzureUpdatesClient>(http =>
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd(AzureUpdatesOptions.UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddStandardResilienceHandler(resilience =>
            {
                // 5xx と 429 を 3 回まで再試行し、1 回の要求は 10 秒で打ち切る
                resilience.Retry.MaxRetryAttempts = 3;
                resilience.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            });
        return services;
    }
}
