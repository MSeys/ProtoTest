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
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using ProtoTest.Sheets;
using ProtoTest.SampleApp;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Sql;
using ProtoTest.Sql.Testcontainers;
using ProtoTest.Web;
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
        var usePostgresContainer = string.Equals(
            demoConfiguration["ProtoTest:Database"],
            "postgres",
            StringComparison.OrdinalIgnoreCase);
        // The store's provider is configuration, not an assumption: an external PostgreSQL connection
        // string must not be handed to the SQLite provider just because no container is owned.
        var postgresStore = usePostgresContainer || IsPostgresStore(
            demoConfiguration["Database:Provider"], configuredDatabase);
        // A configured connection string points at an existing broker; Broker=container lets the run own
        // one. The adapter reads it under its own key, the in-process application under its own - both
        // are filled from the same started container.
        var configuredMessaging = demoConfiguration["ProtoTest:Messaging:RabbitMq:ConnectionString"];
        var useMessagingContainer = string.Equals(
            demoConfiguration["ProtoTest:Messaging:Broker"],
            "container",
            StringComparison.OrdinalIgnoreCase);
        if (useMessagingContainer)
        {
            builder.AddInfrastructure(
                RabbitMqBroker.Container(),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString");
        }

        var useMessaging = useMessagingContainer || !string.IsNullOrWhiteSpace(configuredMessaging);

        if (usePostgresContainer)
        {
            // Owned by the whole run and started with the host; its connection string reaches the
            // test-side domain and the in-process application through infrastructure settings.
            builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
        }

        // Without PostgreSQL the demo owns a file database: several connections can share it, WAL lets
        // readers and the writer work at the same time, and a fresh file per run keeps tenant slugs
        // from colliding with the previous run.
        string? ownedDatabasePath = null;
        if (!postgresStore && configuredDatabase is null)
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

        var fallbackDatabase = configuredDatabase ?? $"Data Source={ownedDatabasePath}";
        var databaseProvider = postgresStore ? "postgres" : "sqlite";
        var composeDomainInTests = hostedInProcess || configuredDatabase is not null || postgresStore;

        if (hostedInProcess && !usePostgresContainer)
        {
            // The standalone instance the browser journeys drive; the host starts it with the run and
            // fills the web session's base URL from it. Its capability is what lets those journeys skip
            // when the suite cannot own the store. A published run (TargetUrl set) drives the published
            // application instead, so starting a local copy would point the journeys at the wrong store.
            builder.AddInfrastructure(new StandaloneSampleApp(fallbackDatabase, databaseProvider));
            builder.AddCapability(new ProtoCapabilityDescriptor(
                "Northstar standalone", ProtoCapabilityKinds.Server, "Demo"));
        }

        if (composeDomainInTests)
        {
            // The test's own composition of the same domain, over a connection ProtoTest owns and
            // releases as a resource. Isolation stays None because the application has its own
            // connection: a test transaction would hide the test's writes from it. Provisioners
            // release what they create.
            builder
                .AddSql(
                    provider => CreateDatabaseConnection(ResolveDatabase(provider, fallbackDatabase), postgresStore),
                    sql => sql.Isolation = SqlIsolation.None)
                .ConfigureServices(services => services.AddNorthstarDomain(
                    (provider, options) =>
                    {
                        var connection = provider.GetRequiredService<DbConnection>();
                        if (postgresStore)
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
            .AddSheets()
            .AddWeb()
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
                        if (!usePostgresContainer)
                        {
                            // A container's connection string arrives as infrastructure settings; otherwise
                            // the configured or run-owned store is passed through here.
                            webHost.UseSetting("ConnectionStrings:Northstar", fallbackDatabase);
                        }

                        webHost.UseSetting("Database:Provider", databaseProvider);
                        webHost.UseSetting("ProtoTest:TestSupport", "true");
                        if (!string.IsNullOrWhiteSpace(configuredMessaging))
                        {
                            // An external broker is configuration, not infrastructure; pass it through.
                            webHost.UseSetting("Messaging:RabbitMq:ConnectionString", configuredMessaging);
                        }
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

        // Registered last on purpose: the application's client initializer runs first and starts the
        // in-process app, which declares its event topology - the messaging initializer then finds the
        // exchanges it binds its per-test taps to.
        if (useMessaging)
        {
            builder.AddMessaging(messaging => messaging.UseRabbitMq());
        }
        else
        {
            builder.AddMessaging();
        }
    }

    /// <summary>
    /// Whether the configured store is PostgreSQL. An explicit <c>Database:Provider</c> wins; otherwise
    /// the connection string decides, and anything that is not clearly PostgreSQL stays SQLite.
    /// </summary>
    internal static bool IsPostgresStore(string? provider, string? connectionString)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return string.Equals(provider, "postgres", StringComparison.OrdinalIgnoreCase)
                || string.Equals(provider, "postgresql", StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        return connectionString.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || connectionString.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDatabase(IServiceProvider provider, string fallback)
        => provider.GetService<ProtoInfrastructureSettings>() is { } settings
            && settings.Values.TryGetValue("ConnectionStrings:Northstar", out var connection)
            ? connection
            : fallback;

    private static DbConnection CreateDatabaseConnection(string connectionString, bool postgres)
        => postgres
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(connectionString);
}

