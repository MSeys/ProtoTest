namespace ProtoTest.Rest;

using Microsoft.Extensions.Configuration;
using ProtoTest.Http;

/// <summary>Controls how REST response bodies are buffered.</summary>
public sealed class RestResponseOptions : ProtoHttpResponseOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Responses";

    internal void Bind(IConfiguration configuration)
        => configuration.GetSection(ConfigurationSectionName).Bind(this);
}
