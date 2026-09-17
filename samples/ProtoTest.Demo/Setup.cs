namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.GraphQL;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "prototest-demo.prototrace"))
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"ProtoTest:Applications:{NorthstarTargets.Api}:OpenApi:Specification"] = Path.Combine(
                        AppContext.BaseDirectory, "northstar.openapi.json"),
                    [$"ProtoTest:Applications:{NorthstarTargets.Api}:Endpoints:GraphQL"] = "/graphql"
                }))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>();
                services.AddSingleton<IGraphQLWebSocketFactory, NorthstarGraphQLWebSocketFactory>();
            })
            .AddTestHook<NorthstarScenarioHook>()
            .AddData(data => data.AddDefaults<NorthstarDataDefaults>())
            .AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>()
            .AddApplication(NorthstarTargets.Api, app => app
                .AddAspNetCoreServer<Program>()
                .AddRest(rest =>
                {
                    rest.AddClient("Api")
                        .AddCollector<RestCoverageCollector>()
                        .AddCollector<OpenApiCoverageCollector>();
                })
                .AddGraphQL(graphQL => graphQL
                    .CaptureAttachments()
                    .AddClient("GraphQL")
                    .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
                    .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "northstar.graphql"))))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "report.html");
                sink.Title = "Northstar Platform · ProtoTest Demo";
            });
    }
}
