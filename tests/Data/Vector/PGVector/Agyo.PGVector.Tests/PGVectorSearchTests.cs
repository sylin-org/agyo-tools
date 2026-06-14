using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;
using Agyo.Data.Vector.PGVector;
using Agyo.Testing.Infrastructure;
using Agyo.Testing.Integration;
using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.PGVector.Tests;

/// <summary>
/// Behavioral KNN round-trip against a real pgvector Postgres. Container-gated via
/// <c>AGYO_PGVECTOR_CONNECTION_STRING</c> (skip-clean when absent). Exercises the full adapter
/// contract: <c>VectorEnsureCreated</c> → <c>UpsertMany</c> (with JSONB metadata) → <c>Search</c>,
/// then asserts the nearest match is the vector aligned with the query direction.
/// </summary>
public sealed class PGVectorSearchTests
{
    private const string ConnEnvVar = "AGYO_PGVECTOR_CONNECTION_STRING";

    // Keep dimensions tiny — the adapter creates a table sized to PGVectorOptions.DefaultDimension,
    // so the spec sets that to 4 to match these probe vectors.
    private const int Dimension = 4;

    private readonly ITestOutputHelper _output;

    public PGVectorSearchTests(ITestOutputHelper output) => _output = output;

    /// <summary>A minimal vector-bearing entity; only <c>Id</c> is required by IEntity&lt;string&gt;.</summary>
    private sealed class ProbeDoc : IEntity<string>
    {
        public string Id { get; set; } = string.Empty;
    }

    [SkippableFact]
    public async Task Upsert_then_KNN_search_returns_the_nearest_vector()
    {
        Skip.IfNot(InfraProbe.Available(ConnEnvVar), InfraProbe.Unavailable(ConnEnvVar));
        var connectionString = InfraProbe.ConnectionString(ConnEnvVar)!;

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Vector:PGVector:ConnectionString", connectionString)
            .WithSetting("Agyo:Vector:PGVector:DefaultDimension", Dimension.ToString())
            // Sequential scan keeps the probe fast and deterministic on a 3-row table.
            .WithSetting("Agyo:Vector:PGVector:AutoCreateIndex", "false")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IVectorAdapterFactory>();
        var repo = factory.Create<ProbeDoc, string>(host.Services);

        // Fresh table for an isolated assertion.
        await repo.VectorEnsureCreated();
        await repo.Flush();

        // Three orthogonal-ish unit vectors. The query points along "east"; "east" must win.
        var east = new[] { 1f, 0f, 0f, 0f };
        var north = new[] { 0f, 1f, 0f, 0f };
        var up = new[] { 0f, 0f, 1f, 0f };

        var inserted = await repo.UpsertMany(new (string, float[], object?)[]
        {
            ("east", east, new { label = "east" }),
            ("north", north, new { label = "north" }),
            ("up", up, new { label = "up" }),
        });
        inserted.Should().Be(3);

        var result = await repo.Search(new VectorQueryOptions(Query: new[] { 0.9f, 0.1f, 0f, 0f }, TopK: 2));

        result.Matches.Should().NotBeEmpty();
        result.Matches[0].Id.Should().Be("east", "the query vector points along the 'east' axis");
        _output.WriteLine(
            $"KNN nearest: {result.Matches[0].Id} (score {result.Matches[0].Score:F4}); " +
            $"returned {result.Matches.Count} of TopK=2");

        // Metadata survives the JSONB round-trip.
        var nearestMeta = result.Matches[0].Metadata as IDictionary<string, object>;
        nearestMeta.Should().NotBeNull();
        nearestMeta!["label"].Should().Be("east");
    }

    [SkippableFact]
    public async Task GetEmbedding_round_trips_an_upserted_vector()
    {
        Skip.IfNot(InfraProbe.Available(ConnEnvVar), InfraProbe.Unavailable(ConnEnvVar));
        var connectionString = InfraProbe.ConnectionString(ConnEnvVar)!;

        await using var host = await AgyoIntegrationHost.Configure()
            .WithSetting("Agyo:Vector:PGVector:ConnectionString", connectionString)
            .WithSetting("Agyo:Vector:PGVector:DefaultDimension", Dimension.ToString())
            .WithSetting("Agyo:Vector:PGVector:AutoCreateIndex", "false")
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IVectorAdapterFactory>();
        var repo = factory.Create<ProbeDoc, string>(host.Services);

        await repo.VectorEnsureCreated();
        await repo.Flush();

        var embedding = new[] { 0.25f, 0.5f, 0.75f, 1f };
        await repo.Upsert("doc-1", embedding, metadata: new { kind = "probe" });

        var fetched = await repo.GetEmbedding("doc-1");

        fetched.Should().NotBeNull();
        fetched!.Should().Equal(embedding);
    }
}
