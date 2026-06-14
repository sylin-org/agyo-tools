using Microsoft.Extensions.DependencyInjection;
using Agyo.Secrets.Abstractions;

namespace Agyo.Secrets.Core.DI;

internal sealed class SecretsBuilder(IServiceCollection services) : ISecretsBuilder
{
    public ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider
    {
        services.AddSingleton<ISecretProvider, T>();
        return this;
    }
}
