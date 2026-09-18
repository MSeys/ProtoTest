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
        // Where the application runs is infrastructure, not test logic. By default it is hosted
        // in-process; set PROTOTEST_TARGET_URL to run the same suite against a published environment
        // or a container, with no change to the tests or the provisioners.
        var targetUrl = System.Environment.GetEnvironmentVariable("PROTOTEST_TARGET_URL");
        var hostedInProcess = string.IsNullOrWhiteSpace(targetUrl);

        // The application and the tests agree on the database the same way they agree on the URL:
        // through configuration. Set PROTOTEST_DATABASE=postgres for a real server in a container;
        // otherwise the suite shares the sample's named in-memory database. Against a published
        // environment the connection string points at that environment's database.
        var configuredDatabase = System.Environment.GetEnvironmentVariable("ConnectionStrings__Northstar");
        var usePostgres = string.Equals(
            System.Environment.GetEnvironmentVariable("PROTOTEST_DATABASE"),
            "postgres",
            StringComparison.OrdinalIgnoreCase);

        PostgresDatabase? postgres = null;
        if (usePostgres && !PostgresDatabase.TryStart(configure: null, out postgres, out var postgresError))
        {
            throw new InvalidOperationException(
                $"PROTOTEST_DATABASE=postgres was requested but the container did not start: {postgresError}");
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

        // The application reads its configuration from the environment, exactly as it would when
        // published, so the tests configure the database the way an operator would - and the test-side
        // domain and the application end up on one store.
        System.Environment.SetEnvironmentVariable("ConnectionStrings__Northstar", northstarDatabase);
        System.Environment.SetEnvironmentVariable("Database__Provider", databaseProvider);
        if (hostedInProcess)
        {
            // Scenario provisioning is a development affordance; the in-process demo enables it.
            System.Environment.SetEnvironmentVariable("PROTOTEST_TEST_SUPPORT", "1");
        }

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

    private static DbConnection CreateDatabaseConnection(string connectionString, bool postgres)
        => postgres
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(connectionString);
}
