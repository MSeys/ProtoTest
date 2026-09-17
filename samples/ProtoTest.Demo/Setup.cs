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
        // Where the application runs is infrastructure, not test logic. By default it is hosted
        // in-process; set PROTOTEST_TARGET_URL to run the same suite against a published environment
        // or a container, with no change to the tests or the provisioners.
        var targetUrl = Environment.GetEnvironmentVariable("PROTOTEST_TARGET_URL");
        var hostedInProcess = string.IsNullOrWhiteSpace(targetUrl);

        builder
            .ConfigureTracing(trace => trace.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "prototest-demo.prototrace"))
            .ConfigureAppConfiguration(configuration =>
            {
                var settings = new Dictionary<string, string?>
                {
                    [$"ProtoTest:Applications:{NorthstarTargets.Api}:OpenApi:Specification"] = Path.Combine(
                        AppContext.BaseDirectory, "northstar.openapi.json"),
                    [$"ProtoTest:Applications:{NorthstarTargets.Api}:Endpoints:GraphQL"] = "/graphql"
                };

                if (!hostedInProcess)
                {
                    settings[$"ProtoTest:Applications:{NorthstarTargets.Api}:BaseUrl"] = targetUrl;
                }

                configuration.AddInMemoryCollection(settings);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>();
                if (hostedInProcess)
                {
                    // In-process subscriptions ride the test server's own WebSocket client.
                    services.AddSingleton<IGraphQLWebSocketFactory, NorthstarGraphQLWebSocketFactory>();
                }
            })
            .AddTestHook<NorthstarScenarioHook>()
            .AddData(data => data.AddDefaults<NorthstarDataDefaults>())
            .AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>()
            .AddApplication(NorthstarTargets.Api, app =>
            {
                if (hostedInProcess)
                {
                    app.AddAspNetCoreServer<Program>();
                }

                app.AddRest(rest =>
                    {
                        rest.AddClient("Api")
                            .AddCollector<RestCoverageCollector>()
                            .AddCollector<OpenApiCoverageCollector>();
                    })
                    .AddGraphQL(graphQL => graphQL
                        .CaptureAttachments()
                        .AddClient("GraphQL")
                        .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
                        .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "northstar.graphql")));
            })
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "report.html");
                sink.Title = "Northstar Platform · ProtoTest Demo";
            });
    }
}
