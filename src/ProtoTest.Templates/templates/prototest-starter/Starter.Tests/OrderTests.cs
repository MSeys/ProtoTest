namespace Starter.Tests;

using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[Application("Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task CreatingAnOrderReturnsIt()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        // A shape is partial: it names what the behaviour depends on and ignores the rest.
        response
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .Should.MatchShape(new
            {
                id = JsonValue.GreaterThan(0),
                product = "notebook",
                quantity = 2,
                status = "pending"
            });
    }

    [ProtoTest]
    public async Task ACreatedOrderCanBeReadBack()
    {
        using var created = await Proto.Context.Rest()
            .Body(new { product = "pencil", quantity = 12 })
            .PostAsync("/api/orders");
        var id = created.Should.HaveHttpStatus(HttpStatusCode.Created).ReadAsJson<OrderId>()!.Id;

        using var response = await Proto.Context.Rest().GetAsync($"/api/orders/{id}");

        response
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { id, product = "pencil", quantity = 12 });
    }

    [ProtoTest]
    public async Task AnOrderNeedsAtLeastOneItem()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 0 })
            .PostAsync("/api/orders");

        response
            .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
            .Should.MatchShape(new { errors = new { quantity = new[] { "Order at least one." } } });
    }

    [ProtoTest]
    public async Task AnUnknownOrderIsNotFound()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders/999999");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }

    private sealed record OrderId(int Id);
}
