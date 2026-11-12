using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace UpdatesCowork.Office;

/// <summary>Office の生成機能を DI に登録する。</summary>
public static class OfficeServiceCollectionExtensions
{
    public static IServiceCollection AddOfficeDocuments(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOfficeDocumentFactory, OfficeDocumentFactory>();
        return services;
    }
}
