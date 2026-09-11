namespace ProtoTest.Rest.Demo;

using System.Net;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.OpenApi;
using ProtoTest.Reporting;
using ProtoTest.Rest;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    internal static WireMockServer Server { get; } = WireMockServer.Start();

    [OneTimeTearDown]
    public void StopServer()
    {
        Server.Stop();
        Server.Dispose();
    }

    protected override void Configure(IProtoHostBuilder builder)
    {
        ConfigureApi();

        builder
            .ConfigureAppConfiguration(configuration =>
            {
                configuration
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ProtoTest:Clients:Orders:BaseUrl"] = Server.Url,
                        ["ProtoTest:Clients:Orders:OpenApi:Specification"] = Path.Combine(
                            AppContext.BaseDirectory,
                            "orders.openapi.json"),
                        ["ProtoTest:Clients:Inventory:BaseUrl"] = Server.Url
                    });
            })
            .AddRest(rest =>
            {
                rest.CaptureAttachments(options =>
                {
                    options.CaptureRequestBodies = true;
                    options.CaptureExpectedShapes = true;
                });
                rest.AddClient("Orders")
                    .WithCollector<RestCoverageCollector>()
                    .WithCollector<OpenApiCoverageCollector>();
                rest.AddClient("Inventory")
                    .WithCollector<RestCoverageCollector>();
                rest.AddClient(
                        "SaaS",
                        context => context.Context<DemoEnvironmentContext>().BaseUri)
                    .WithCollector<RestCoverageCollector>();
            })
            .AddSink<JsonReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Rest.Demo", "report.json"))
            .AddSink<HtmlReportSink>(sink => sink.OutputPath = Path.Combine(
                "TestResults", "ProtoTest.Rest.Demo", "report.html"));
    }

    private static void ConfigureApi()
    {
        Server
            .Given(Request.Create()
                .WithPath("/orders/42")
                .UsingGet())
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Unauthorized));

        Server
            .Given(Request.Create()
                .WithPath("/orders/42")
                .UsingGet()
                .WithHeader("Authorization", "Bearer orders-token"))
            .AtPriority(1)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new Order(42, "confirmed", 129.95m)));

        Server
            .Given(Request.Create()
                .WithPath("/orders")
                .UsingPost())
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Unauthorized));

        Server
            .Given(Request.Create()
                .WithPath("/orders")
                .UsingPost()
                .WithHeader("Authorization", "Bearer orders-token"))
            .AtPriority(1)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new Order(43, "pending", 75m)));

        Server
            .Given(Request.Create()
                .WithPath("/inventory/notebook")
                .UsingGet())
            .AtPriority(10)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Unauthorized));

        Server
            .Given(Request.Create()
                .WithPath("/inventory/notebook")
                .UsingGet()
                .WithHeader("Authorization", "Bearer inventory-token"))
            .AtPriority(1)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new InventoryItem("notebook", 27)));

        Server
            .Given(Request.Create()
                .WithPath("/orders/999")
                .UsingGet())
            .AtPriority(1)
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.NotFound)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new ErrorResponse("order-not-found")));

        Server
            .Given(Request.Create()
                .WithPath("/test-support/users")
                .UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.Created)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new ProvisionedUser(
                    "billing.admin@example.test",
                    "billing-admin",
                    "user-billing-admin-token")));

        Server
            .Given(Request.Create()
                .WithPath("/billing/invoices")
                .WithParam("state", "open")
                .WithHeader("X-Tenant", "demo")
                .WithHeader("Authorization", "Bearer user-billing-admin-token")
                .UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(HttpStatusCode.OK)
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new
                {
                    Tenant = "demo",
                    State = "open",
                    Invoices = new[] { new { Id = 701, Total = 49.95m } }
                }));
    }

    private sealed record Order(int Id, string Status, decimal Total);

    private sealed record ErrorResponse(string Error);

    private sealed record InventoryItem(string Sku, int Available);

    private sealed record ProvisionedUser(string Email, string Role, string AccessToken);
}
