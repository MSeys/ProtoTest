namespace ProtoTest.AspNetCore.Demo;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.SampleApi;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Json;

[RestClient("OrderApi")]
public class OrderApiTests
{
    [ProtoTest]
    public async Task GetPing_ReturnsExpectedPayload()
    {
        using var response = await Proto.Context.Rest().GetAsync("/ping");

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { message = "pong" });
    }

    [ProtoTest]
    public async Task GetMessage_UsesApplicationDependencyInjection()
    {
        using var response = await Proto.Context.Rest().GetAsync("/message");

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { message = "Hello from the test service override!" });
    }

    [ProtoTest]
    public async Task CreateOrder_ExercisesRequestBodyAndCreatedResponse()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/orders");

        response
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new
            {
                id = 101,
                product = "notebook",
                quantity = 2,
                status = JsonValue.OneOf("pending", "confirmed")
            });
    }

    [ProtoTest]
    public async Task CreateOrder_ReturnsDomainValidationError()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 0 })
            .PostAsync("/orders");

        response
            .ShouldHaveStatus(HttpStatusCode.BadRequest)
            .ShouldMatchShape(new { error = "quantity-must-be-positive" });
    }

    [ProtoTest]
    public void CreateServerScope_IsolatesScopedApplicationServices()
    {
        using var firstScope = Proto.Context.CreateServerScope<Program>("OrderApi");
        using var secondScope = Proto.Context.CreateServerScope<Program>("OrderApi");

        var first = firstScope.ServiceProvider.GetRequiredService<IScenarioIdProvider>();
        var second = secondScope.ServiceProvider.GetRequiredService<IScenarioIdProvider>();

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }
}
