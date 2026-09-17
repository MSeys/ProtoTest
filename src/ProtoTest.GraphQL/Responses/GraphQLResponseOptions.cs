namespace ProtoTest.GraphQL;

using ProtoTest.Core;
using ProtoTest.Http;

public sealed class GraphQLResponseOptions : ProtoHttpResponseOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:GraphQL:Responses";
    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;
}
