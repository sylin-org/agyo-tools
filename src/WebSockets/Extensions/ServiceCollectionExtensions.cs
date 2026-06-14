using System;
using Koan.Core.Modules;
using Agyo.WebSockets.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Agyo.WebSockets.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWebSocketStreamAdapters(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddKoanOptions<WebSocketStreamOptions>(Constants.Configuration.Section);
        services.TryAddSingleton<IWebSocketStreamFactory, WebSocketStreamFactory>();

        return services;
    }
}
