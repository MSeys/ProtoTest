using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;

namespace Starter.Tests;

[Application("Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task Creating_an_order_returns_it()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        // A shape is partial: it names what the behaviour depends on and ignores the rest.
        response
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new
            {
                id = JsonValue.GreaterThan(0),
                product = "notebook",
                quantity = 2,
                status = "pending"
            });
    }

    [ProtoTest]
    public async Task A_created_order_can_be_read_back()
    {
        using var created = await Proto.Context.Rest()
            .Body(new { product = "pencil", quantity = 12 })
            .PostAsync("/api/orders");
        var id = created.Should.HaveHttpStatus(HttpStatusCode.Created).ReadAsJson<OrderId>()!.Id;

        using var response = await Proto.Context.Rest().GetAsync($"/api/orders/{id}");

        response
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { id, product = "pencil", quantity = 12 });
    }

    [ProtoTest]
    public async Task An_order_needs_at_least_one_item()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 0 })
            .PostAsync("/api/orders");

        response
            .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
            .ShouldMatchShape(new { errors = new { quantity = new[] { "Order at least one." } } });
    }

    [ProtoTest]
    public async Task An_unknown_order_is_not_found()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders/999999");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }

    private sealed record OrderId(int Id);
}
