namespace ProtoTest.Mcp.Tests;

using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

/// <summary>
/// One real MCP session over an in-process stream pair: the production registration
/// (<see cref="ProtoTestMcpBuilderExtensions.AddProtoTestMcp"/>) and the tools through the SDK's
/// client, so the tests exercise tool discovery, schema binding and the error conversion too.
/// </summary>
internal sealed class McpSession : IAsyncDisposable
{
    private readonly Pipe _clientToServer;
    private readonly Pipe _serverToClient;
    private readonly CancellationTokenSource _shutdown;
    private readonly ServiceProvider _provider;
    private readonly Task _serverTask;

    private McpSession(
        Pipe clientToServer,
        Pipe serverToClient,
        CancellationTokenSource shutdown,
        ServiceProvider provider,
        Task serverTask,
        McpClient client)
    {
        _clientToServer = clientToServer;
        _serverToClient = serverToClient;
        _shutdown = shutdown;
        _provider = provider;
        _serverTask = serverTask;
        Client = client;
    }

    public McpClient Client { get; }

    public static async Task<McpSession> StartAsync(ProtoTestMcpOptions options)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var shutdown = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .AddProtoTestMcp(options);
        var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<McpServer>();
        var serverTask = server.RunAsync(shutdown.Token);
        try
        {
            var client = await McpClient.CreateAsync(new StreamClientTransport(
                serverInput: clientToServer.Writer.AsStream(),
                serverOutput: serverToClient.Reader.AsStream()));
            return new McpSession(clientToServer, serverToClient, shutdown, provider, serverTask, client);
        }
        catch
        {
            await StopAsync(clientToServer, serverToClient, shutdown, provider, serverTask);
            throw;
        }
    }

    /// <summary>Calls a tool and returns the raw result, error results included.</summary>
    public async Task<CallToolResult> CallToolAsync(string tool, Dictionary<string, object?>? arguments = null)
        => await Client.CallToolAsync(tool, arguments ?? [], cancellationToken: CancellationToken.None);

    /// <summary>Calls a tool that is expected to succeed and parses its JSON payload.</summary>
    public async Task<JsonElement> CallJsonAsync(string tool, Dictionary<string, object?>? arguments = null)
        => Json(await CallToolAsync(tool, arguments));

    /// <summary>The JSON payload of a tool result.</summary>
    public static JsonElement Json(CallToolResult result)
    {
        using var document = JsonDocument.Parse(Text(result));
        return document.RootElement.Clone();
    }

    /// <summary>The concatenated text content of a tool result.</summary>
    public static string Text(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await StopAsync(_clientToServer, _serverToClient, _shutdown, _provider, _serverTask);
        _shutdown.Dispose();
    }

    private static async Task StopAsync(
        Pipe clientToServer,
        Pipe serverToClient,
        CancellationTokenSource shutdown,
        ServiceProvider provider,
        Task serverTask)
    {
        await shutdown.CancelAsync();
        await clientToServer.Writer.CompleteAsync();
        await serverToClient.Writer.CompleteAsync();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
        }

        await provider.DisposeAsync();
    }
}
