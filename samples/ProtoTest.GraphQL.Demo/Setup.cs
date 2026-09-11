namespace ProtoTest.GraphQL.Demo;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.SampleApp;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    internal const string Api = "GraphQLDemo";
    private const string Server = "GraphQLDemoServer";

    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            .AddAspNetCoreServer<Program>(Server)
            .AddGraphQL(graphQL => graphQL
                .AddClientFrom(Api, Server)
                .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "sampleapp.graphql")))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine("TestResults", "GraphQL", "report.json"));
    }
}
