namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// Moves the tenant's virtual clock. A deliberate test-support affordance: no public API can close a
/// billing period, and the application must advance the same clock its own code reads.
/// </summary>
public sealed class NorthstarClockProvisioner : IProtoDataProvisioner<AdvanceClockRequest, ClockResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<ClockResponse>> CreateAsync(
        AdvanceClockRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        await TestSupportProbe.EnsureAvailableAsync(context.Execution);
        var organization = context.Execution.Resolve<NorthstarOrganizationContext>();
        using var response = await context.Execution.Rest()
            .WithoutAuth()
            .Body(value)
            .PostAsync(
                "/test-support/tenants/{tenant}/clock/advance",
                new { tenant = organization.Tenant },
                ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var clock = response.ReadAsJson<ClockResponse>()
            ?? throw new InvalidOperationException("The sample app returned no clock state.");
        return new ProtoDataProvisioningResult<ClockResponse>(clock, organization.Tenant);
    }
}

/// <summary>
/// Creates an in-process webhook sink whose response the test controls. A deliberate test-support
/// affordance: it exists only inside the running application, never on the product surface.
/// </summary>
public sealed class NorthstarWebhookSinkProvisioner
    : IProtoDataProvisioner<ConfigureWebhookSinkRequest, WebhookSinkResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<WebhookSinkResponse>> CreateAsync(
        ConfigureWebhookSinkRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        await TestSupportProbe.EnsureAvailableAsync(context.Execution);
        using var response = await context.Execution.Rest()
            .WithoutAuth()
            .Body(value)
            .PostAsync("/test-support/webhook-sinks", ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var sink = response.ReadAsJson<WebhookSinkResponse>()
            ?? throw new InvalidOperationException("The sample app returned no webhook sink.");
        return new ProtoDataProvisioningResult<WebhookSinkResponse>(sink, sink.Id);
    }
}

/// <summary>
/// Provisions a tenant over the test-support surface when no domain store is reachable, and removes it
/// again when the test ends. The fallback keeps a published environment runnable.
/// </summary>
public sealed class NorthstarTestSupportTenantProvisioner
    : IProtoDataProvisioner<ProvisionTenantRequest, TenantResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<TenantResponse>> CreateAsync(
        ProvisionTenantRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        await TestSupportProbe.EnsureAvailableAsync(context.Execution);
        using var response = await context.Execution.Rest()
            .WithoutAuth()
            .Body(value)
            .PostAsync("/test-support/tenants", ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var tenant = response.ReadAsJson<TenantResponse>()
            ?? throw new InvalidOperationException("The sample app provisioned no tenant.");
        return new ProtoDataProvisioningResult<TenantResponse>(
            tenant,
            tenant.Tenant,
            new DeleteTenantOverTestSupport(context.Execution, tenant.Tenant));
    }

    private sealed class DeleteTenantOverTestSupport(ProtoExecutionContext execution, string slug)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var response = await execution.Rest()
                .WithoutAuth()
                .DeleteAsync("/test-support/tenants/{tenant}", new { tenant = slug });
            response.Should.HaveHttpStatus(HttpStatusCode.NoContent);
        }
    }
}

/// <summary>
/// Creates a member for a role and mints the token the test acts with, over the test-support surface
/// when no domain store is reachable. The public API cannot hand out a token, so this is the fallback
/// for <c>[SignedInAs]</c> against a published environment.
/// </summary>
public sealed class NorthstarTestSupportMemberProvisioner
    : IProtoDataProvisioner<InviteMemberRequest, TestMemberResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<TestMemberResponse>> CreateAsync(
        InviteMemberRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        await TestSupportProbe.EnsureAvailableAsync(context.Execution);
        var organization = context.Execution.Resolve<NorthstarOrganizationContext>();
        using var response = await context.Execution.Rest()
            .WithoutAuth()
            .Body(value)
            .PostAsync(
                "/test-support/tenants/{tenant}/members",
                new { tenant = organization.Tenant },
                ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var member = response.ReadAsJson<TestMemberResponse>()
            ?? throw new InvalidOperationException("The sample app provisioned no member.");
        return new ProtoDataProvisioningResult<TestMemberResponse>(member, member.Membership.Id);
    }
}
