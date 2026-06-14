using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agyo.WebSockets;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Agyo.WebSockets.Tests;

/// <summary>
/// Behavioral spec: a real ASP.NET Core pipeline (TestServer) accepts a WebSocket upgrade through
/// <see cref="HttpContextWebSocketExtensions.AcceptWebSocketStream"/>, treats the connection as a
/// duplex <c>Stream</c>, and echoes a message. The TestServer WebSocket client performs a full
/// send + receive round-trip and the spec asserts the payload survives the trip unchanged.
/// </summary>
public sealed class WebSocketStreamEchoTests
{
    private readonly ITestOutputHelper _output;

    public WebSocketStreamEchoTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AcceptWebSocketStream_EchoesDuplexMessage()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost
                    .UseTestServer()
                    .Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Run(async context =>
                        {
                            if (!context.WebSockets.IsWebSocketRequest)
                            {
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                return;
                            }

                            // Capability under test: accept the upgrade as a duplex WebSocketStream
                            // (framed as Text so a single message round-trips cleanly), read the
                            // inbound message, and echo it straight back.
                            var streamOptions = new WebSocketStreamOptions
                            {
                                MessageType = WebSocketMessageType.Text
                            };

                            await using var stream =
                                await context.AcceptWebSocketStream(streamOptions, context.RequestAborted);

                            var buffer = new byte[1024];
                            var read = await stream.ReadAsync(buffer.AsMemory(), context.RequestAborted);
                            if (read > 0)
                            {
                                await stream.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
                                await stream.FlushAsync(context.RequestAborted);
                            }
                        });
                    });
            })
            .StartAsync();

        var server = host.GetTestServer();
        var wsClient = server.CreateWebSocketClient();

        var uri = new UriBuilder(server.BaseAddress) { Scheme = "ws" }.Uri;
        using var client = await wsClient.ConnectAsync(uri, CancellationToken.None);

        const string payload = "duplex-roundtrip-ping";
        var outbound = Encoding.UTF8.GetBytes(payload);

        await client.SendAsync(
            outbound.AsMemory(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);

        var receiveBuffer = new byte[1024];
        var result = await client.ReceiveAsync(receiveBuffer.AsMemory(), CancellationToken.None);

        var echoed = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
        _output.WriteLine($"Sent: {payload} | Received: {echoed} ({result.MessageType}, eom={result.EndOfMessage})");

        result.MessageType.Should().Be(WebSocketMessageType.Text);
        echoed.Should().Be(payload, "the server must echo the duplex message back unchanged");

        // Graceful close so the server loop unwinds cleanly.
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }
}
