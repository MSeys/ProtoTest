namespace ProtoTest.Mcp;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// The stdio MCP host: stdout carries the protocol and nothing else, diagnostics go to stderr. The
/// server reads local archives read-only and binds no port.
/// </summary>
public static class McpHost
{
    /// <summary>Runs the stdio server until the client closes it. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(error);

        ProtoTestMcpOptions options;
        try
        {
            options = ProtoTestMcpOptions.Resolve(
                args,
                Environment.GetEnvironmentVariable(ProtoTestMcpOptions.ProjectEnvironmentVariable),
                Directory.GetCurrentDirectory());
        }
        catch (ProtoTestMcpConfigurationException exception)
        {
            await error.WriteLineAsync(exception.Message).ConfigureAwait(false);
            return 2;
        }

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .AddProtoTestMcp(options);

        using var host = builder.Build();
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
