namespace ProtoTest.Rest.Demo;

using System.Net;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.NUnit;
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
                        ["ProtoTest:Clients:Inventory:BaseUrl"] = Server.Url
                    });
            })
            .AddRest(rest => rest
                .AddClient("Orders")
                .WithCollector<RestCoverageCollector>())
            .AddRest(rest => rest
                .AddClient("Inventory")
                .WithCollector<RestCoverageCollector>());
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
    }

    private sealed record Order(int Id, string Status, decimal Total);

    private sealed record ErrorResponse(string Error);

    private sealed record InventoryItem(string Sku, int Available);
}
