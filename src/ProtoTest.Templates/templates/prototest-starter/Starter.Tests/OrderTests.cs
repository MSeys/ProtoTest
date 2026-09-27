namespace Starter.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.Rest;
#if (runner == "nunit")
using NUnit.Framework;
using ProtoTest.NUnit;
#elif (runner == "xunit")
using ProtoTest.Xunit;
using Xunit;
#elif (runner == "xunit3")
using ProtoTest.Xunit3;
using Xunit;
#elif (runner == "mstest")
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.MSTest;
#endif

#if (runner == "xunit")
[Collection(ProtoTestCollection.Name)]
#elif (runner == "mstest")
[TestClass]
#endif
[Application("Api")]
public sealed class OrderTests
{
#if (runner == "nunit" || runner == "mstest")
    [ProtoTest]
#elif (runner == "xunit" || runner == "xunit3")
    [ProtoTestFact]
#elif (runner == "tunit")
    [Test]
#endif
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

#if (runner == "nunit" || runner == "mstest")
    [ProtoTest]
#elif (runner == "xunit" || runner == "xunit3")
    [ProtoTestFact]
#elif (runner == "tunit")
    [Test]
#endif
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

#if (runner == "nunit" || runner == "mstest")
    [ProtoTest]
#elif (runner == "xunit" || runner == "xunit3")
    [ProtoTestFact]
#elif (runner == "tunit")
    [Test]
#endif
    public async Task AnOrderNeedsAtLeastOneItem()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 0 })
            .PostAsync("/api/orders");

        response
            .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
            .Should.MatchShape(new { errors = new { quantity = new[] { "Order at least one." } } });
    }

#if (runner == "nunit" || runner == "mstest")
    [ProtoTest]
#elif (runner == "xunit" || runner == "xunit3")
    [ProtoTestFact]
#elif (runner == "tunit")
    [Test]
#endif
    public async Task AnUnknownOrderIsNotFound()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders/999999");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }

    private sealed record OrderId(int Id);
}
