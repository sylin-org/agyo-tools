using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Agyo.Testing.Integration;
using Agyo.WebSockets;
using AwesomeAssertions;
using Koan.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.WebSockets.Tests;

/// <summary>
/// ARCH-0079 boot smoke: a real <see cref="Microsoft.Extensions.Hosting.IHost"/> with
/// <c>services.AddKoan()</c> must, through reflective discovery of
/// <see cref="Agyo.WebSockets.Initialization.KoanAutoRegistrar"/>, register the WebSocket stream
/// factory surface. No hand-registration of the capability happens here — only AddKoan().
/// </summary>
public sealed class WebSocketStreamBootSmokeTests
{
    private readonly ITestOutputHelper _output;

    public WebSocketStreamBootSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AddKoan_RegistersWebSocketStreamFactory()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetService<IWebSocketStreamFactory>();

        factory.Should().NotBeNull(
            "the Agyo.WebSockets KoanAutoRegistrar should register IWebSocketStreamFactory via reflective AddKoan() discovery");

        // The registrar wires a concrete singleton (TryAddSingleton); resolving twice returns the same instance.
        var second = host.Services.GetRequiredService<IWebSocketStreamFactory>();
        second.Should().BeSameAs(factory, "the factory is registered as a singleton");

        _output.WriteLine($"Resolved IWebSocketStreamFactory: {factory!.GetType().FullName}");
    }

    [Fact]
    public async Task ResolvedFactory_ProducesWebSocketStreamInstances()
    {
        await using var host = await AgyoIntegrationHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

        var factory = host.Services.GetRequiredService<IWebSocketStreamFactory>();

        // Exercise the resolved surface without any network: the factory wraps a WebSocket as a
        // System.Net.WebSockets.WebSocketStream. This proves the resolved service is a functioning
        // WebSocketStreamFactory (it constructs the BCL stream type), not just a registration token.
        // The connected read/write/flush path is covered end-to-end by WebSocketStreamEchoTests; we
        // deliberately do not read/write/flush here because the BCL stream gates I/O on socket state
        // and a fresh ClientWebSocket is not connected.
        using var clientWebSocket = new ClientWebSocket();
        var bidirectional = factory.Create(clientWebSocket);
        var writable = factory.CreateWritable(clientWebSocket, WebSocketMessageType.Text);
        var readable = factory.CreateReadable(clientWebSocket);

        bidirectional.Should().BeAssignableTo<System.IO.Stream>("a WebSocketStream is a Stream");
        writable.Should().NotBeNull();
        readable.Should().NotBeNull();
        bidirectional.Should().NotBeSameAs(writable, "each factory call constructs a fresh stream wrapper");
    }
}
