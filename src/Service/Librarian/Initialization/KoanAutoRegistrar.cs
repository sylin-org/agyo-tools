using Agyo.Service.Librarian.Infrastructure;
using Agyo.Service.Librarian.Models;
using Agyo.Service.Librarian.Services;
using Agyo.Service.Librarian.Services.Hooks;
using Agyo.Service.Librarian.Services.Maintenance;
using Agyo.Service.Librarian.Utilities;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agyo.Service.Librarian.Initialization;

/// <summary>
/// Auto-registrar for Agyo.Service.Librarian services
/// </summary>
/// <remarks>
/// Registers all ingest pipeline services following Koan's auto-discovery pattern.
/// Stateless services (Extraction, TokenCounter, etc.) are singletons.
/// Stateful services (Indexer, Search) are scoped for per-request isolation.
/// </remarks>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Agyo.Service.Librarian";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register stateless pipeline services (singleton - configuration only)
        services.AddSingleton<Extraction>();

        // Register stateful pipeline services (scoped - per-request)
        services.AddScoped<Discovery>();
        services.AddScoped<Chunker>();
        services.AddScoped<IndexingPlanner>();
        services.AddScoped<Embedding>(sp =>
        {
            var ai = sp.GetRequiredService<Koan.AI.Contracts.IAiPipeline>();
            var cache = sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Embedding>>();

            // TODO: Get default model from configuration
            var defaultModel = "all-minilm";

            return new Embedding(ai, cache, logger, defaultModel);
        });
        services.AddScoped<Indexer>();
        services.AddSingleton<ChunkMaintenanceService>();
        services.AddScoped<Search>();

        // Register Phase 1 AI-first services
        services.AddSingleton<TokenCounter>();
        services.AddSingleton<Pagination>();
        services.AddSingleton<UrlBuilder>();

        // Register background services
        services.AddSingleton<TagSeedInitializer>();
        services.AddHostedService(sp => sp.GetRequiredService<TagSeedInitializer>());
        services.AddHostedService<VectorSyncWorker>();

        // Scheduled maintenance task (folded from the former JobMaintenanceTaskRegistration initializer, ARCH-0086).
        services.AddSingleton<Agyo.Scheduling.IScheduledTask, Agyo.Service.Librarian.Tasks.JobMaintenanceTask>();

        services.AddScoped<Koan.Web.Hooks.IModelHook<TagVocabularyEntry>, TagVocabularyHooks>();
        services.AddScoped<Koan.Web.Hooks.IModelHook<TagRule>, TagRuleHooks>();
        services.AddScoped<Koan.Web.Hooks.IModelHook<TagPipeline>, TagPipelineHooks>();
        services.AddScoped<Koan.Web.Hooks.IModelHook<SearchPersona>, SearchPersonaHooks>();
        services.AddScoped<Agyo.Service.Librarian.Filters.PartitionScopeFilter>();

        // --- Service/runtime registrations (moved out of Program.cs so the whole service is composed
        //     by AddKoan() — "Reference = Intent". Program.cs keeps only the web-host pipeline). ---
        services.AddOptions<FileMonitoringOptions>()
            .BindConfiguration(ConfigurationConstants.FileMonitoring.Section);
        services.AddOptions<ProjectResolutionOptions>()
            .BindConfiguration(ConfigurationConstants.ProjectResolution.Section);

        services.AddSingleton<PathValidator>();
        services.AddSingleton<ProjectResolver>();
        services.AddSingleton<IncrementalIndexer>();
        services.AddSingleton<IndexingCoordinator>();
        services.AddSingleton<IIndexingResumptionQueue, IndexingResumptionQueue>();
        services.AddSingleton<Metrics>();
        services.AddSingleton<TagResolver>();
        services.AddScoped<ISearchService>(sp => sp.GetRequiredService<Search>());
        services.AddScoped<IndexProjectAsync>(sp =>
        {
            var indexer = sp.GetRequiredService<Indexer>();
            return (string projectId, bool force, CancellationToken cancellationToken, IProgress<IndexingProgress>? progress) =>
                indexer.IndexProjectAsync(projectId, progress, cancellationToken, force);
        });

        services.AddSingleton<MetricsCollector>();
        services.AddSingleton<EnhancedMetrics>();

        services.AddHostedService<FileMonitoringService>();
        services.AddHostedService<IndexingResumptionWorker>();
        services.AddSingleton<FileMonitoringService>(sp =>
            (FileMonitoringService)sp.GetServices<IHostedService>().First(s => s is FileMonitoringService));

        // Add memory cache if not already registered
        services.AddMemoryCache();
    }

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
        module.AddNote("Ingest pipeline: Discovery, Extraction, Chunking, Embedding, Indexing");
        module.AddNote("Transactional Outbox: VectorSyncWorker (at-least-once delivery)");
    }
}
