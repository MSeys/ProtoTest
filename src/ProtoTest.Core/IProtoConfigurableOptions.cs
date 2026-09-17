namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;

/// <summary>
/// An options (or sink) type whose properties can be layered with values from a fixed
/// "ProtoTest:..." configuration section after code-based defaults are applied.
/// </summary>
public interface IProtoConfigurableOptions
{
    /// <summary>The configuration section this type binds from, e.g. "ProtoTest:Rest:Responses".</summary>
    string ConfigurationSectionName { get; }
}

public static class ProtoConfigurableOptionsExtensions
{
    /// <summary>Binds configuration over the code-based defaults already set on <paramref name="options"/>.</summary>
    public static void BindFromConfiguration(this IProtoConfigurableOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.GetSection(options.ConfigurationSectionName).Bind(options);
    }
}
