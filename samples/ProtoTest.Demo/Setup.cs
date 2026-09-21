namespace ProtoTest.Demo;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Northstar.ProtoTest;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
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
using ProtoTest.SampleApp.Domain;
using ProtoTest.Sql;
using ProtoTest.Sql.Testcontainers;
using ProtoTest.Web;
using System.Data.Common;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        var configuration = LoadConfiguration();
        var environment = DemoEnvironment.From(configuration);

        environment.PrepareOwnedDatabase();
        var messagingBroker = ConfigureInfrastructure(builder, environment);
        ConfigureLocalApplications(builder, environment, messagingBroker);
        ConfigureDomain(builder, environment);
        ConfigureProtoTest(builder, environment);
        ConfigureMessaging(builder, environment);
    }

    private static IConfiguration LoadConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    private static IProtoConnectionInfrastructure? ConfigureInfrastructure(
        IProtoHostBuilder builder,
        DemoEnvironment environment)
    {
        IProtoConnectionInfrastructure? messagingBroker = null;
        if (environment.OwnsMessagingBroker)
        {
            // The standalone application and the messaging adapter share the broker owned by this run.
            messagingBroker = RabbitMqBroker.Container();
            builder.AddInfrastructure(
                messagingBroker,
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString");
        }

        if (environment.OwnsPostgres)
        {
            builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar");
        }

        return messagingBroker;
    }

    private static void ConfigureLocalApplications(
        IProtoHostBuilder builder,
        DemoEnvironment environment,
        IProtoConnectionInfrastructure? messagingBroker)
    {
        if (environment.RunsStandaloneConsole)
        {
            // Browser journeys use a real process. Published runs point at their configured target instead.
            builder.AddInfrastructure(new StandaloneSampleApp(
                environment.DatabaseConnection,
                environment.DatabaseProvider,
                () => messagingBroker?.ConnectionString ?? environment.ConfiguredMessaging));
            builder.AddCapability(new ProtoCapabilityDescriptor(
                "Northstar standalone", ProtoCapabilityKinds.Server, "Demo"));
        }

        if (environment.UsesLocalApplications)
        {
            builder.AddInfrastructure(new NorthstarConsoleBuild());
        }
    }

    private static void ConfigureDomain(IProtoHostBuilder builder, DemoEnvironment environment)
    {
        if (!environment.CanComposeDomain) return;

        // The application has its own connection, so a test transaction would hide fixture writes.
        builder
            .AddSql(
                provider => CreateDatabaseConnection(
                    ResolveDatabase(provider, environment.DatabaseConnection),
                    environment.UsesPostgres),
                sql => sql.Isolation = SqlIsolation.None)
            .ConfigureServices(services => services.AddNorthstarDomain(
                (provider, options) =>
                {
                    var connection = provider.GetRequiredService<DbConnection>();
                    if (environment.UsesPostgres)
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

    private static void ConfigureProtoTest(IProtoHostBuilder builder, DemoEnvironment environment)
    {
        builder
            .ConfigureTracing(trace =>
            {
                trace.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "prototest-demo.prototrace");
                trace.ActivitySources.Add("Northstar.Domain");
            })
            .ConfigureAppConfiguration(configuration => ConfigureTests(configuration, environment))
            .AddSheets()
            .AddWeb()
            .AddNorthstarTestSupport(support =>
                support.UseInProcessGraphQLWebSockets = environment.UsesLocalApplications)
            .AddNorthstarData(data =>
                data.UseDomainProvisioners = environment.CanComposeDomain)
            .AddRunGate("no error findings", context => context
                .ItemsOfKind(ProtoReportItemKinds.Finding)
                .Any(item => item.Status == ProtoReportStatus.Error)
                ? ProtoRunGateResult.Failed("The run recorded error findings.")
                : ProtoRunGateResult.Passed("No error findings were recorded."))
            .AddApplication(NorthstarTargets.Api, app => ConfigureApplication(app, environment))
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "ProtoTest.Demo", "report.html");
                sink.Title = "Northstar Platform · ProtoTest Demo";
            });
    }

    private static void ConfigureTests(
        IConfigurationBuilder configuration,
        DemoEnvironment environment)
    {
        configuration.AddConfiguration(environment.Configuration);
        var settings = new Dictionary<string, string?>
        {
            [$"ProtoTest:Applications:{NorthstarTargets.Api}:OpenApi:Specification"] = Path.Combine(
                AppContext.BaseDirectory, "northstar.openapi.json"),
            [$"ProtoTest:Applications:{NorthstarTargets.Api}:Endpoints:GraphQL"] = "/graphql",
            ["ProtoTest:Web:Pages:Source"] = ConsoleBuild.SourceFolder,
            ["ProtoTest:Web:Pages:Framework"] = "vue",
            ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
        };

        if (!environment.UsesLocalApplications)
        {
            settings[$"ProtoTest:Applications:{NorthstarTargets.Api}:BaseUrl"] = environment.TargetUrl;
        }

        configuration.AddInMemoryCollection(settings);
    }

    private static void ConfigureApplication(
        IProtoApplicationBuilder app,
        DemoEnvironment environment)
    {
        if (environment.UsesLocalApplications)
        {
            app.AddAspNetCoreServer<Program>(configureWebHost: webHost =>
            {
                if (!environment.OwnsPostgres)
                {
                    webHost.UseSetting("ConnectionStrings:Northstar", environment.DatabaseConnection);
                }

                webHost.UseSetting("Database:Provider", environment.DatabaseProvider);
                webHost.UseSetting("ProtoTest:TestSupport", "true");

                // Container settings are forwarded by the host; external configuration is passed explicitly.
                if (!string.IsNullOrWhiteSpace(environment.ConfiguredMessaging))
                {
                    webHost.UseSetting(
                        "Messaging:RabbitMq:ConnectionString",
                        environment.ConfiguredMessaging);
                }
            });
        }

        app.AddRest(rest =>
            {
                rest.CaptureAttachments()
                    .AddClient("Api")
                    .AddCollector<RestCoverageCollector>()
                    .AddCollector<OpenApiCoverageCollector>();
            })
            .AddGraphQL(graphQL => graphQL
                .CaptureAttachments()
                .AddClient("GraphQL")
                .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
                .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "northstar.graphql")))
            .AddGrpc(grpc => grpc.CaptureAttachments().AddClient("Projects"));
    }

    private static void ConfigureMessaging(IProtoHostBuilder builder, DemoEnvironment environment)
    {
        // Registered last because the application declares the topology before messaging binds its taps.
        if (environment.UsesMessaging)
        {
            builder.AddMessaging(messaging => messaging.CaptureAttachments().UseRabbitMq());
        }
        else
        {
            builder.AddMessaging(messaging => messaging.CaptureAttachments());
        }
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
