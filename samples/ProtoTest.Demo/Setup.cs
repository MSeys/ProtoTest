namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
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
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "control-plane.prototrace"))
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [$"ProtoTest:Clients:{SampleAppTargets.Api}:OpenApi:Specification"] = Path.Combine(
                        AppContext.BaseDirectory, "control-plane.openapi.json")
                }))
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>();
                services.AddSingleton<IGraphQLWebSocketFactory, SampleAppGraphQLWebSocketFactory>();
            })
            .AddTestHook<SaasScenarioHook>()
            .AddRest(rest => rest
                .AddClient(SampleAppTargets.Api)
                .WithCollector<RestCoverageCollector>()
                .WithCollector<OpenApiCoverageCollector>())
            .AddAspNetCoreServer<Program>(SampleAppTargets.Api)
            .AddGraphQL(graphQL => graphQL
                .CaptureAttachments()
                .AddClientFrom(SampleAppTargets.GraphQL, SampleAppTargets.Api)
                .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
                .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "control-plane.graphql")))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "report.html");
                sink.Title = "Northstar Control Plane · ProtoTest Demo";
            });
    }
}
