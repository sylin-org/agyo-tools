using System;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Logging;
using Koan.Core.Provenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agyo.Translation.Initialization;

/// <summary>
/// Reference = Intent registrar: referencing Agyo.Translation registers the in-process
/// <see cref="TranslationService"/> so the static <see cref="Translation"/> facade can resolve it
/// from the ambient host. No service mesh, no manifest — just a single in-process instance.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly KoanLog.KoanLogScope Log = KoanLog.For<KoanAutoRegistrar>();

    public string ModuleName => "Agyo.Translation";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        Log.BootDebug(LogActions.Init, "loaded", ("module", ModuleName));

        services.AddSingleton<TranslationService>();

        Log.BootDebug(LogActions.Init, "services-registered", ("module", ModuleName));
    }

    public void Describe(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(module);

        module.Describe(ModuleVersion);
    }

    private static class LogActions
    {
        public const string Init = "registrar.init";
    }
}
