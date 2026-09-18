namespace ProtoTest.Demo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.GraphQL;
using ProtoTest.Grpc;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Sql;
using ProtoTest.Sql.Testcontainers;
using System.Data.Common;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // Where the application runs and which store it uses are infrastructure, chosen from
        // configuration and nothing else: ProtoTest:TargetUrl points the same suite at a published
        // environment, ProtoTest:Database=postgres owns a container, ConnectionStrings:Northstar
        // points at that environment's store, and the demo's opt-in failure lives under ProtoTest:Demo.
        var demoConfiguration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var targetUrl = demoConfiguration["ProtoTest:TargetUrl"];
        var hostedInProcess = string.IsNullOrWhiteSpace(targetUrl);
        var configuredDatabase = demoConfiguration.GetConnectionString("Northstar");
        var usePostgres = string.Equals(
            demoConfiguration["ProtoTest:Database"],
            "postgres",
            StringComparison.OrdinalIgnoreCase);

        PostgresDatabase? postgres = null;
        if (usePostgres && !PostgresDatabase.TryStart(configure: null, out postgres, out var postgresError))
        {
            throw new InvalidOperationException(
                $"ProtoTest:Database=postgres was requested but the container did not start: {postgresError}");
        }

        if (postgres is not null)
        {
            // Owned by the whole run: started once here, released when the host is disposed.
            builder.AddResource(postgres);
        }

        // Without PostgreSQL the demo owns a file database: several connections can share it, WAL lets
        // readers and the writer work at the same time, and a fresh file per run keeps tenant slugs
        // from colliding with the previous run.
        string? ownedDatabasePath = null;
        if (postgres is null && configuredDatabase is null)
        {
            ownedDatabasePath = Path.GetFullPath(Path.Combine("TestResults", "ProtoTest.Demo", "northstar-demo.db"));
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(ownedDatabasePath + suffix))
                {
                    File.Delete(ownedDatabasePath + suffix);
                }
            }
        }

        var northstarDatabase = postgres?.ConnectionString
            ?? configuredDatabase
            ?? $"Data Source={ownedDatabasePath}";
        var databaseProvider = postgres is not null ? "postgres" : "sqlite";
        var composeDomainInTests = hostedInProcess || configuredDatabase is not null || postgres is not null;

        if (composeDomainInTests)
        {
            // The test's own composition of the same domain, over a connection ProtoTest owns and
            // releases as a resource. Isolation stays None because the application has its own
            // connection: a test transaction would hide the test's writes from it. Provisioners
            // release what they create.
            builder
                .AddSql(
                    _ => CreateDatabaseConnection(northstarDatabase, postgres is not null),
                    sql => sql.Isolation = SqlIsolation.None)
                .ConfigureServices(services => services.AddNorthstarDomain(
                    (provider, options) =>
                    {
                        var connection = provider.GetRequiredService<DbConnection>();
                        if (postgres is not null)
                        {
                            options.UseNpgsql(connection);
                        }
                        else
                        {
                            options.UseSqlite(connection);
                        }
                    },
                    ServiceLifetime.Scoped));
        }

        builder
            .ConfigureTracing(trace =>
            {
                trace.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "prototest-demo.prototrace");
                // Watch the application's own instrumentation the way any OpenTelemetry consumer would.
                trace.ActivitySources.Add("Northstar.Domain");
            })
            .ConfigureAppConfiguration(configuration =>
            {
                // Everything the demo chose above is visible to the tests through configuration.
                configuration.AddConfiguration(demoConfiguration);
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
            .AddRunGate("no error findings", context => context
                .ItemsOfKind(ProtoReportItemKinds.Finding)
                .Any(item => item.Status == ProtoReportStatus.Error)
                ? ProtoRunGateResult.Failed("The run recorded error findings.")
                : ProtoRunGateResult.Passed("No error findings were recorded."))
            .AddData(data => data.AddDefaults<NorthstarDataDefaults>())
            .AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>()
            .AddDataProvisioner<CreateProjectRequest, ProjectResponse, NorthstarDomainProjectProvisioner>()
            .AddApplication(NorthstarTargets.Api, app =>
            {
                if (hostedInProcess)
                {
                    // The in-process application receives the same choices through host settings -
                    // no process-wide environment variables involved.
                    app.AddAspNetCoreServer<Program>(configureWebHost: webHost =>
                    {
                        webHost.UseSetting("ConnectionStrings:Northstar", northstarDatabase);
                        webHost.UseSetting("Database:Provider", databaseProvider);
                        webHost.UseSetting("ProtoTest:TestSupport", "true");
                    });
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
                        .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "northstar.graphql")))
                    .AddGrpc(grpc => grpc.AddClient("Projects"));
            })
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "report.html");
                sink.Title = "Northstar Platform · ProtoTest Demo";
            });
    }

    private static DbConnection CreateDatabaseConnection(string connectionString, bool postgres)
        => postgres
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(connectionString);
}
