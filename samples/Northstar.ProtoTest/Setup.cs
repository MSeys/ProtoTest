namespace Northstar.ProtoTest;

using System.Data.Common;
using global::NUnit.Framework;
using global::ProtoTest.AspNetCore;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.GraphQL;
using global::ProtoTest.Messaging;
using global::ProtoTest.Messaging.RabbitMq;
using global::ProtoTest.Messaging.RabbitMq.Testcontainers;
using global::ProtoTest.NUnit;
using global::ProtoTest.OpenApi;
using global::ProtoTest.Reporting;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Domain;
using global::ProtoTest.Sheets;
using global::ProtoTest.Sql;
using global::ProtoTest.Sql.Testcontainers;
using global::ProtoTest.Testcontainers;
using global::ProtoTest.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NorthstarProgram = global::ProtoTest.SampleApp.Program;

/// <summary>
/// Composes the Learning demo once for the whole run: the application in-process for the journeys that
/// control the clock and on a loopback listener for the browser journey, the run-owned store and
/// broker, a trace and the reports. The store and broker choices read as a composition: configured
/// address first, then a container this run owns, then the run-owned SQLite file and no broker.
/// </summary>
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        var configuration = LoadConfiguration();
        var run = NorthstarRun.From(configuration);
        run.PrepareOwnedStore();

        ConfigureInfrastructure(builder, run);
        ConfigureApplications(builder, run);
        ConfigureDomain(builder, run);

        builder
            .ConfigureTracing(trace =>
            {
                trace.ActivitySources.Add("Northstar.Domain");
            })
            .ConfigureRedaction(redaction => redaction.AddSensitiveName("OwnerToken"))
            .ConfigureAppConfiguration(settings => ConfigureTests(settings, run))
            .AddSheets()
            .AddNorthstarTestSupport()
            .AddNorthstarData(data => data.UseDomainProvisioners = run.CanComposeDomain)
            .AddCapabilityReason(
                ProtoCapabilityKinds.Broker,
                "No broker is configured; set ProtoTest:Messaging:Broker=container.")
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "Northstar.ProtoTest", "report.json"))
            .AddSink<HtmlReportSink>(sink =>
            {
                sink.OutputPath = Path.Combine("TestResults", "Northstar.ProtoTest", "report.html");
                sink.Title = "Northstar Learning demo";
            })
            .AddRunGate("no error findings", context => context
                .ItemsOfKind(ProtoReportItemKinds.Finding)
                .Any(item => item.Status == ProtoReportStatus.Error)
                ? ProtoRunGateResult.Failed("The run recorded error findings.")
                : ProtoRunGateResult.Passed("No error findings were recorded."));

        ConfigureMessaging(builder, run);
    }

    private static IConfiguration LoadConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

    private static void ConfigureInfrastructure(IProtoHostBuilder builder, NorthstarRun run)
    {
        if (run.OwnsPostgres)
        {
            // UseConfigured wins when the environment provides the address, so a container never starts.
            builder.AddInfrastructure(
                "NorthstarDatabase",
                chain => chain
                    .UseConfigured()
                    .UseContainer(PostgresDatabase.Container()),
                "ConnectionStrings:Northstar");
        }

        if (run.OwnsMessagingBroker)
        {
            // The integration's option key and the application's own key are filled by the same piece.
            builder.AddInfrastructure(
                "MessagingBroker",
                chain => chain
                    .UseConfigured()
                    .UseContainer(RabbitMqBroker.Container()),
                RabbitMqOptions.ConnectionStringSetting,
                "Messaging:RabbitMq:ConnectionString");
        }
    }

    private static void ConfigureApplications(IProtoHostBuilder builder, NorthstarRun run)
    {
        builder.AddApplication(NorthstarTargets.Api, app =>
        {
            if (run.RunsLocalApplications)
            {
                // The in-process server is what carries the test clock into the application.
                app.AddAspNetCoreServer<NorthstarProgram>(configureWebHost: webHost =>
                    ConfigureHostedApplication(webHost, run));
            }

            app.AddRest(rest => rest
                    .CaptureAttachments()
                    .AddClient(NorthstarTargets.Api)
                    .AddCollector<RestCoverageCollector>()
                    .AddCollector<RestTrafficCoverageCollector>()
                    // Endpoints, responses and properties of the contract the application serves.
                    .AddCollector<OpenApiCoverageCollector>("/openapi/v1.json"))
                .AddGraphQL(graphQL => graphQL
                    .CaptureAttachments()
                    .AddClient("GraphQL", endpoint: "GraphQL")
                    // Types, fields and arguments of the schema the application serves.
                    .WithSchemaCoverage("/graphql?sdl"));
        });

        if (run.RunsLocalApplications)
        {
            // The browser needs a real listener; the page journey follows this instance's address.
            builder.AddLoopbackApplication(NorthstarTargets.Web, NorthstarProgram.CreateApp);
            builder.AddHttpReadiness(NorthstarTargets.Web, "/health");
        }

        builder.AddApplication(NorthstarTargets.Web, app => app
            .AddRest(rest => rest
                .CaptureAttachments()
                .AddClient(NorthstarTargets.Web))
            .AddWeb(options => options.Headless = true));
    }

    private static void ConfigureHostedApplication(IWebHostBuilder webHost, NorthstarRun run)
    {
        webHost.UseSetting("Database:Provider", run.DatabaseProvider);
        webHost.UseSetting("ProtoTest:TestSupport", "true");
        if (run.DatabaseConnection is { } store)
        {
            webHost.UseSetting("ConnectionStrings:Northstar", store);
        }

        if (!string.IsNullOrWhiteSpace(run.ConfiguredMessaging))
        {
            webHost.UseSetting("Messaging:RabbitMq:ConnectionString", run.ConfiguredMessaging);
        }
    }

    private static void ConfigureTests(IConfigurationBuilder settings, NorthstarRun run)
    {
        settings.AddConfiguration(run.Configuration);
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = run.DatabaseProvider,
            // The loopback instance serves the page while the in-process instance owns webhook dispatch.
            ["Northstar:DisableWebhookDispatcher"] = "true",
            [$"ProtoTest:Applications:{NorthstarTargets.Api}:Endpoints:GraphQL"] = "/graphql",
            [$"ProtoTest:Applications:{NorthstarTargets.Web}:Endpoints:GraphQL"] = "/graphql"
        };

        if (run.RunsLocalApplications)
        {
            if (run.DatabaseConnection is { } store)
            {
                values["ConnectionStrings:Northstar"] = store;
            }
        }
        else
        {
            values[$"{ProtoApplication.SectionPath}:{NorthstarTargets.Api}:BaseUrl"] = run.TargetUrl;
            values[$"{ProtoApplication.SectionPath}:{NorthstarTargets.Web}:BaseUrl"] = run.TargetUrl;
        }

        if (!string.IsNullOrWhiteSpace(run.ConfiguredMessaging))
        {
            // The application reads its own key; the suite keeps the ProtoTest one.
            values["Messaging:RabbitMq:ConnectionString"] = run.ConfiguredMessaging;
        }

        settings.AddInMemoryCollection(values);
    }

    private static void ConfigureDomain(IProtoHostBuilder builder, NorthstarRun run)
    {
        if (!run.CanComposeDomain)
        {
            return;
        }

        // The application has its own connection, so a test transaction would hide fixture writes.
        builder
            .AddSql(
                provider => CreateDatabaseConnection(
                    ResolveDatabase(provider, run.DatabaseConnection ?? string.Empty),
                    run.UsesPostgres),
                sql => sql.Isolation = SqlIsolation.None)
            .ConfigureServices(services =>
            {
                services.AddNorthstarDomain(
                    (provider, options) =>
                    {
                        var connection = provider.GetRequiredService<DbConnection>();
                        if (run.UsesPostgres)
                        {
                            options.UseNpgsql(connection);
                        }
                        else
                        {
                            options.UseSqlite(connection);
                        }
                    },
                    ServiceLifetime.Scoped);
                // The host owns the test clock bridge; the domain's own TimeProvider would shadow it,
                // so the suite's composition keeps the bridge and the application keeps the test clock.
                services.RemoveAll<TimeProvider>();
            });
    }

    private static void ConfigureMessaging(IProtoHostBuilder builder, NorthstarRun run)
    {
        // Registered last so the application exists before messaging binds its taps. The tap is declared
        // in code so it is bound during setup, not at the first await, and does not miss a publish that
        // happens before the test awaits.
        if (run.UsesMessaging)
        {
            builder.AddMessaging(messaging => messaging
                .CaptureAttachments()
                .UseRabbitMq()
                .Declare("invoice.paid")
                .Tap("invoice.paid"));
        }
        else
        {
            builder.AddMessaging(messaging => messaging.CaptureAttachments());
        }
    }

    private static string ResolveDatabase(IServiceProvider provider, string fallback)
        => ProtoApplication.ResolveSetting(
               provider.GetRequiredService<IConfiguration>(),
               provider.GetService<ProtoInfrastructureSettings>(),
               "ConnectionStrings:Northstar")
           ?? fallback;

    private static DbConnection CreateDatabaseConnection(string connectionString, bool postgres)
        => postgres
            ? new NpgsqlConnection(connectionString)
            : new SqliteConnection(connectionString);
}
