namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;

/// <summary>Controls how REST response bodies are buffered.</summary>
public sealed class RestResponseOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Responses";

    /// <summary>Maximum response body size buffered by the convenience API. Defaults to 10 MiB.</summary>
    public int MaxResponseBodyBytes { get; set; } = 10 * 1024 * 1024;

    internal void Bind(IConfiguration configuration)
        => configuration.GetSection(ConfigurationSectionName).Bind(this);
}
