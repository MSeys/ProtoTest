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

    /// <summary>
    /// An optional older section name a suite may still configure, so a key the type has since renamed
    /// keeps working. The fallback binds first and <see cref="ConfigurationSectionName"/> binds over it,
    /// so a value under the current section wins. Defaults to <see langword="null"/> (no fallback). An
    /// integration documents a renamed key as deprecated and removes the fallback at the next major.
    /// </summary>
    string? FallbackConfigurationSectionName => null;

    /// <summary>
    /// Validates the resolved options once, after code callbacks and configuration have been applied.
    /// A bad value is a configuration error and must throw here rather than fail the first test that
    /// happens to use it. The default does nothing.
    /// </summary>
    void Validate()
    {
    }
}

public static class ProtoConfigurableOptionsExtensions
{
    /// <summary>Binds configuration over the code-based defaults already set on <paramref name="options"/>.</summary>
    public static void BindFromConfiguration(this IProtoConfigurableOptions options, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);

        // A renamed section keeps working: the deprecated fallback binds first, then the current section
        // binds over it, so a value under ConfigurationSectionName wins over the legacy key.
        if (!string.IsNullOrWhiteSpace(options.FallbackConfigurationSectionName))
        {
            configuration.GetSection(options.FallbackConfigurationSectionName!).Bind(options);
        }

        configuration.GetSection(options.ConfigurationSectionName).Bind(options);
    }
}
