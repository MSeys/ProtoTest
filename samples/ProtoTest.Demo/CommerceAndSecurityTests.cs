namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class CommerceAndSecurityTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]
    public async Task BillingAdministratorCanCreateOrderAndReconcileOpenInvoices()
    {
        using var created = await Proto.Context.Rest()
            .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
            .PostAsync("/api/orders");
        created.ShouldHaveStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            id = JsonValue.GreaterThan(0), product = "observability-seat", quantity = 12,
            total = JsonValue.GreaterThan(200m), status = "pending"
        });

        using var invoices = await Proto.Context.Rest().GetAsync("/api/billing/invoices", new { state = "open" });
        invoices.ShouldHaveStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            state = "open",
            invoices = new[] { new { id = JsonValue.GreaterThan(0), state = "open", total = JsonValue.GreaterThan(0m) } }
        });
    }

    [ProtoTest]
    [SampleUser]
    public async Task TenantBoundaryRejectsValidTokenFromDifferentOrganization()
    {
        var user = Proto.Context.Context<SampleUserContext>();
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .Header("Authorization", $"Bearer {user.AccessToken}")
            .Header("X-Tenant", "competitor-tenant")
            .GetAsync("/api/control-plane");
        response.ShouldHaveStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { error = "tenant-access-denied" });
    }
}
