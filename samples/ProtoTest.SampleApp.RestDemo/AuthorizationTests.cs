namespace ProtoTest.SampleApp.RestDemo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Json;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[RestClient(SampleAppTargets.Api)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
public sealed class AuthorizationTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]
    public async Task BillingAdministrator_CanSeeOpenInvoices()
    {
        var environment = Proto.Context.Context<SampleEnvironmentContext>();
        using var response = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices", new { state = "open" });

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                tenant = environment.Tenant,
                state = "open",
                invoices = new[]
                {
                    new { id = JsonValue.GreaterThan(0), state = "open", total = JsonValue.GreaterThan(0m) }
                }
            });
    }

    [ProtoTest]
    [SampleUser]
    public async Task Member_CannotSeeBillingInformation()
    {
        using var response = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices");

        response
            .ShouldHaveStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { error = "insufficient-permissions" });
    }

    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task TenantAdministrator_CanListProvisionedUsers()
    {
        var user = Proto.Context.Context<SampleUserContext>();
        using var response = await Proto.Context.Rest().GetAsync("/api/admin/users");

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                tenant = user.Tenant,
                users = new[]
                {
                    new { id = user.Id, email = user.Email, role = SampleRoles.TenantAdministrator }
                }
            });
    }
}
