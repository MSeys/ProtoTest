namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // Where the application runs is infrastructure, not test logic. By default it is hosted
        // in-process; set PROTOTEST_TARGET_URL to run the same suite against a published environment
        // or a container, with no change to the tests or the provisioners.
        var targetUrl = System.Environment.GetEnvironmentVariable("PROTOTEST_TARGET_URL");
        var hostedInProcess = string.IsNullOrWhiteSpace(targetUrl);

        // The application and the tests agree on the database the same way they agree on the URL:
        // through configuration. In-process they share the sample's named in-memory database; against
        // a published environment the connection string points at that environment's database.
        var configuredDatabase = System.Environment.GetEnvironmentVariable("ConnectionStrings__Northstar");
        var northstarDatabase = configuredDatabase
            ?? "Data Source=file:northstar;Mode=Memory;Cache=Shared;Pooling=False";
        var composeDomainInTests = hostedInProcess || configuredDatabase is not null;

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

                if (composeDomainInTests)
                {
                    // The test's own composition of the same domain over the same database, so data can
                    // be arranged and verified through domain logic rather than only through the API.
                    services.AddNorthstarDomain(options => options.UseSqlite(northstarDatabase));
                }
            })
            .AddTestHook<NorthstarScenarioHook>()
            .AddData(data => data.AddDefaults<NorthstarDataDefaults>())
            .AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>()
            .AddDataProvisioner<CreateProjectRequest, ProjectResponse, NorthstarDomainProjectProvisioner>()
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
