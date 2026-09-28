namespace ProtoTest.Mcp;

using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

/// <summary>Adds the ProtoTest evidence tools to one MCP server.</summary>
public static class ProtoTestMcpBuilderExtensions
{
    /// <summary>
    /// Registers the read-only ProtoTest tools over the evidence the options point at. The same call
    /// serves the stdio host and any other transport a host supplies.
    /// </summary>
    public static IMcpServerBuilder AddProtoTestMcp(this IMcpServerBuilder builder, ProtoTestMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        builder.Services.AddSingleton(options);
        return builder.WithTools<ProtoTestMcpTools>();
    }
}
