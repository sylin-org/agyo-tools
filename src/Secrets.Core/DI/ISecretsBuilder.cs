using Agyo.Secrets.Abstractions;

namespace Agyo.Secrets.Core.DI;

public interface ISecretsBuilder
{
    ISecretsBuilder AddProvider<T>() where T : class, ISecretProvider;
}
