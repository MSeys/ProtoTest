namespace ProtoTest.SampleApp.RestDemo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Rest.Matching;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class OrderTests
{
    [ProtoTest]
    [SampleUser]
    public async Task Member_CanCreateAndReadAnOrderWithinItsTenant()
    {
        var environment = Proto.Context.Context<SampleEnvironmentContext>();
        using var created = await Proto.Context.Rest()
            .Body(new CreateOrderRequest("notebook", 2, 12.50m))
            .PostAsync("/api/orders");

        created
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new
            {
                id = JsonValue.GreaterThan(0),
                tenant = environment.Tenant,
                product = "notebook",
                quantity = 2,
                total = 25m,
                status = "pending"
            });
        var order = created.ReadAsJson<OrderResponse>()!;

        using var retrieved = await Proto.Context.Rest()
            .GetAsync("/api/orders/{id}", new { order.Id });

        retrieved
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new { id = order.Id, tenant = environment.Tenant, total = 25m });
    }

    [ProtoTest]
    [SampleUser]
    public async Task InvalidOrder_ReturnsADomainError()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateOrderRequest("notebook", 0, 12.50m))
            .PostAsync("/api/orders");

        response
            .ShouldHaveStatus(HttpStatusCode.BadRequest)
            .ShouldMatchShape(new { error = "invalid-order" });
    }

    [ProtoTest]
    [SampleUser]
    public async Task User_CannotCrossTheTenantBoundary()
    {
        var user = Proto.Context.Context<SampleUserContext>();
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .Header("Authorization", $"Bearer {user.AccessToken}")
            .Header("X-Tenant", "another-tenant")
            .GetAsync("/api/orders/{id}", new { id = 123 });

        response
            .ShouldHaveStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { error = "tenant-access-denied" });
    }
}
