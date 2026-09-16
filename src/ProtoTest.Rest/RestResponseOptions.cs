namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>Controls how REST response bodies are buffered.</summary>
public sealed class RestResponseOptions : ProtoHttpResponseOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Rest:Responses";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
