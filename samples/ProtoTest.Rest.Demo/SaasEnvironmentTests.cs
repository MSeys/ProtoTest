namespace ProtoTest.Rest.Demo;

using System.Net;
using System.Net.Http.Headers;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Json;

[RestClient("SaaS")]
[DemoEnvironment(Order = -200)]
[DemoUser("billing-admin", Order = -100)]
[Auth<DemoUserAuthenticator>]
public sealed class SaasEnvironmentTests
{
    [ProtoTest]
    public async Task BillingAdministrator_CanListOpenInvoicesInItsEnvironment()
    {
        var environment = Proto.Context.Context<DemoEnvironmentContext>();
        var user = Proto.Context.Context<DemoUserContext>();

        using var response = await Proto.Context.Rest()
            .Header("X-Tenant", environment.Tenant)
            .GetAsync("/billing/invoices", new { state = "open" });

        response
            .ShouldHaveStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                tenant = environment.Tenant,
                state = "open",
                invoices = new[]
                {
                    new { id = JsonValue.GreaterThan(0), total = JsonValue.GreaterThan(0m) }
                }
            });

        Assert.That(user.Role, Is.EqualTo("billing-admin"));
    }
}

public sealed record DemoEnvironmentContext(Uri BaseUri, string Tenant) : IProtoContext;

public sealed record DemoUserContext(
    string Email,
    string Role,
    string AccessToken) : IProtoContext;

public sealed class DemoEnvironmentAttribute : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        context.SetContext(new DemoEnvironmentContext(new Uri(Setup.Server.Url!), "demo"));
        return Task.CompletedTask;
    }
}

public sealed class DemoUserAttribute(string role) : ProtoAttribute
{
    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        using var response = await context.Rest("SaaS")
            .WithoutAuth()
            .Body(new { role })
            .PostAsync("/test-support/users");

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var user = response.ReadAsJson<ProvisionedUser>()
            ?? throw new InvalidOperationException("The demo user API returned no user.");
        context.SetContext(new DemoUserContext(user.Email, user.Role, user.AccessToken));
    }

    private sealed record ProvisionedUser(string Email, string Role, string AccessToken);
}

public sealed class DemoUserAuthenticator : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(
        RestAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var user = context.Test.Context<DemoUserContext>();
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", user.AccessToken);
        return ValueTask.CompletedTask;
    }
}
