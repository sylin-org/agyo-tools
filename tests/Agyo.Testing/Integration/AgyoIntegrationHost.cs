using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Agyo.Testing.Integration;

/// <summary>
/// Canonical entry point for agyo-tools integration tests, following Koan's ARCH-0079 pattern
/// (ported from Koan's tests/Shared/Koan.Testing/Integration/KoanIntegrationHost, which is not
/// packaged). Builds a real <see cref="IHost"/> with config seeded from a dictionary, lets the
/// test supply additional service registrations (typically <c>services.AddKoan()</c>), and
/// produces an <c>await using</c>-disposable host.
/// </summary>
/// <remarks>
/// Bootstrap-agnostic by design: the test supplies <c>AddKoan()</c> so this helper serves every
/// capability suite without being opinionated about which assemblies are referenced. A real
/// <see cref="IHost"/> (not a bare <c>BuildServiceProvider()</c>) is required so hosted services
/// and <c>IHostApplicationLifetime</c> exist — Koan's reflective bootstrap relies on them. The
/// environment defaults to <c>"Test"</c> (never Production, which trips the relational DDL guard
/// and stops durable adapters from auto-creating their schema).
/// </remarks>
public static class AgyoIntegrationHost
{
    /// <summary>Start a fluent configuration chain for a new integration host.</summary>
    public static Builder Configure() => new();

    public sealed class Builder
    {
        private readonly Dictionary<string, string?> _settings = new(StringComparer.Ordinal);
        private Action<IServiceCollection>? _configureServices;
        private Action<IConfigurationBuilder>? _configureAppConfiguration;
        private string _environment = "Test";

        /// <summary>Set a single configuration key/value pair (merged into in-memory config).</summary>
        public Builder WithSetting(string key, string? value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Configuration key cannot be empty.", nameof(key));
            _settings[key] = value;
            return this;
        }

        /// <summary>Override the host environment name (default "Test"). A non-production environment is
        /// required for relational adapters to auto-create schema in integration tests.</summary>
        public Builder WithEnvironment(string environment)
        {
            if (string.IsNullOrWhiteSpace(environment)) throw new ArgumentException("Environment cannot be empty.", nameof(environment));
            _environment = environment;
            return this;
        }

        /// <summary>Merge a dictionary of configuration key/value pairs into in-memory config.</summary>
        public Builder WithSettings(IEnumerable<KeyValuePair<string, string?>> settings)
        {
            if (settings is null) throw new ArgumentNullException(nameof(settings));
            foreach (var kvp in settings)
            {
                _settings[kvp.Key] = kvp.Value;
            }
            return this;
        }

        /// <summary>
        /// Extend the configuration pipeline with additional sources (env vars, JSON files, etc.).
        /// Runs after the in-memory <see cref="WithSetting"/> seed so later sources can override.
        /// </summary>
        public Builder ConfigureAppConfiguration(Action<IConfigurationBuilder> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            _configureAppConfiguration += configure;
            return this;
        }

        /// <summary>
        /// Register services. Typically called as <c>.ConfigureServices(s => s.AddKoan())</c> to
        /// trigger full reflective discovery, or with additional manual registrations for tests
        /// that need to inject mocks.
        /// </summary>
        public Builder ConfigureServices(Action<IServiceCollection> configure)
        {
            if (configure is null) throw new ArgumentNullException(nameof(configure));
            _configureServices += configure;
            return this;
        }

        /// <summary>Build the host without starting hosted services.</summary>
        public IntegrationHost Build()
        {
            var host = new HostBuilder()
                .UseEnvironment(_environment)
                .ConfigureAppConfiguration(cfg =>
                {
                    if (_settings.Count > 0) cfg.AddInMemoryCollection(_settings);
                    _configureAppConfiguration?.Invoke(cfg);
                })
                .ConfigureServices(services => _configureServices?.Invoke(services))
                .Build();

            return new IntegrationHost(host);
        }

        /// <summary>Build the host AND start hosted services.</summary>
        public async Task<IntegrationHost> StartAsync(CancellationToken ct = default)
        {
            var host = Build();
            await host.StartAsync(ct).ConfigureAwait(false);
            return host;
        }
    }
}

/// <summary>
/// Test-friendly handle over an <see cref="IHost"/>. Implements <see cref="IAsyncDisposable"/>
/// so test code can use <c>await using</c>.
/// </summary>
public sealed class IntegrationHost : IAsyncDisposable
{
    private readonly IHost _host;
    private bool _disposed;

    internal IntegrationHost(IHost host) { _host = host; }

    /// <summary>The DI container produced by the host.</summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>The underlying <see cref="IHost"/> for tests that need direct lifecycle control.</summary>
    public IHost Host => _host;

    /// <summary>Start the host's hosted services.</summary>
    public Task StartAsync(CancellationToken ct = default) => _host.StartAsync(ct);

    /// <summary>Stop the host's hosted services gracefully.</summary>
    public Task StopAsync(CancellationToken ct = default) => _host.StopAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await _host.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        catch { /* teardown is best-effort */ }
        _host.Dispose();
    }
}
