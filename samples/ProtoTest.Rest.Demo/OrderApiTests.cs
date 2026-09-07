namespace ProtoTest.Rest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Rest.Matching;

[BearerToken("orders-token")]
public sealed class OrderApiTests
{
    [ProtoTest]
    [RestClient("Orders")]
    public async Task GetOrder_ReturnsExpectedOrder()
    {
        var response = await Proto.Context.Rest()
            .GetAsync("/orders/{id}", new { id = 42 });

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                id = 42,
                status = "confirmed",
                total = 129.95m
            });
    }

    [ProtoTest]
    [RestClient("Orders")]
    public async Task CreateOrder_SendsJsonBodyAndReturnsCreatedOrder()
    {
        var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/orders");

        response
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new
            {
                id = 43,
                status = IsRest.Regex("^(confirmed|pending)$"),
                total = 75m
            });
    }

    [ProtoTest]
    public async Task GetOrder_ReturnsNotFoundForUnknownOrder()
    {
        var response = await Proto.Context.Rest("Orders")
            .GetAsync("/orders/{id}", new { id = 999 });

        response
            .ShouldHaveStatus(HttpStatusCode.NotFound)
            .ShouldMatchShape(new { error = "order-not-found" });
    }
}
