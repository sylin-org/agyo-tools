using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Hosting.Bootstrap;
using Agyo.Scheduling.Infrastructure;
using static Koan.Core.Hosting.Bootstrap.ProvenancePublicationModeExtensions;

namespace Agyo.Scheduling.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Agyo.Scheduling";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Options: Koan:Scheduling
        services.AddKoanOptions<SchedulingOptions>(cfg, ConfigurationConstants.Section)
            .PostConfigure(opts =>
            {
                // Dev default enabled, Prod default disabled unless explicitly enabled
                if (!env.IsDevelopment() && !cfg.GetSection(ConfigurationConstants.Section).Exists())
                {
                    opts.Enabled = false;
                }
            });

        // Tasks are expected to self-register via Koan.Core IKoanInitializer in their own assemblies.
        // SchedulingOrchestrator is a [KoanBackgroundService] KoanFluentServiceBase: Koan's
        // KoanBackgroundServiceOrchestrator discovers it by attribute and owns its lifecycle. We
        // register the type as a singleton (so that single owner resolves one shared instance) and
        // deliberately do NOT also AddHostedService<>() it — doing both ran ExecuteCore twice on two
        // distinct instances, firing every task twice.
        services.AddSingleton<SchedulingOrchestrator>();
    }

    // Required by IKoanInitializer; minimal registration without bespoke discovery.
    public void Initialize(IServiceCollection services)
    {
        services.AddKoanOptions<SchedulingOptions>(ConfigurationConstants.Section);
        services.AddSingleton<SchedulingOrchestrator>();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        var enabledOption = Configuration.ReadWithSource<bool?>(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.Enabled), null);
        var schedulingSectionExists = cfg.GetSection(ConfigurationConstants.Section).Exists();
        var effectiveEnabled = enabledOption.UsedDefault
            ? (env.IsDevelopment() || schedulingSectionExists)
            : enabledOption.Value ?? true;

        var enabledMode = enabledOption.UsedDefault
            ? ProvenancePublicationMode.Auto
            : FromConfigurationValue(enabledOption);

        module.AddSetting(
            SchedulingProvenanceItems.Enabled,
            enabledMode,
            effectiveEnabled,
            sourceKey: enabledOption.ResolvedKey,
            usedDefault: enabledOption.UsedDefault);

        var readinessOption = Configuration.ReadWithSource(cfg, ConfigurationConstants.FullKey(ConfigurationConstants.Keys.ReadinessGate), true);
        module.AddSetting(
            SchedulingProvenanceItems.ReadinessGate,
            FromConfigurationValue(readinessOption),
            readinessOption.Value,
            sourceKey: readinessOption.ResolvedKey,
            usedDefault: readinessOption.UsedDefault);
        // Discovery count omitted; tasks self-register using Koan.Core initialization.
    }
}
