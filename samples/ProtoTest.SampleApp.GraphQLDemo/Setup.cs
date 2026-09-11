namespace ProtoTest.SampleApp.GraphQLDemo;

using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.NUnit;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Testing;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            .AddRest(rest => rest.AddClient(SampleAppTargets.Api))
            .AddAspNetCoreServer<Program>(SampleAppTargets.Api)
            .AddGraphQL(graphQL => graphQL
                .AddClientFrom(SampleAppTargets.GraphQL, SampleAppTargets.Api)
                .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "sampleapp.graphql")))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.SampleApp.GraphQL", "report.html");
                sink.Title = "ProtoTest Sample SaaS — GraphQL";
            });
    }
}
