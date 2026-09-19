namespace ProtoTest.SampleApp.Testing;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

/// <summary>Provisions an isolated Northstar organization and removes it after the test.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class NorthstarTenantAttribute : ProtoAttribute
{
    public NorthstarTenantAttribute(string planId = PlanIds.Free)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        PlanId = planId;
        Order = -200;
    }

    public string PlanId { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        await TestSupportProbe.EnsureAvailableAsync(context);
        var name = $"northstar-{context.TestId}";
        using var response = await context.Rest()
            .WithoutAuth()
            .Body(new ProvisionTenantRequest(name, PlanId))
            .PostAsync("/test-support/tenants");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var tenant = response.ReadAsJson<TenantResponse>()
            ?? throw new InvalidOperationException("The sample app provisioned no tenant.");
        context.SetContext(new NorthstarOrganizationContext(
            tenant.Tenant,
            tenant.OrganizationId,
            tenant.OwnerEmail,
            tenant.OwnerToken,
            tenant.ApiBaseUrl));
    }

    public override async Task AfterTestAsync(ProtoExecutionContext context)
    {
        var organization = context.TryResolve<NorthstarOrganizationContext>();
        if (organization is null)
        {
            return;
        }

        using var response = await context.Rest()
            .WithoutAuth()
            .DeleteAsync("/test-support/tenants/{tenant}", new { tenant = organization.Tenant });
        response.Should.HaveHttpStatus(HttpStatusCode.NoContent);
    }
}

/// <summary>Acts as a member with the given role for the duration of the test.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SignedInAsAttribute : ProtoAttribute
{
    public SignedInAsAttribute(string role = MemberRoles.Owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Order = -100;
    }

    public string Role { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var organization = context.Resolve<NorthstarOrganizationContext>();
        if (Role == MemberRoles.Owner)
        {
            context.SetContext(new NorthstarMemberContext(
                "owner",
                organization.OwnerEmail,
                MemberRoles.Owner,
                organization.OwnerToken));
            return;
        }

        var email = $"{Role}.{context.TestId}@example.test";
        using var response = await context.Rest()
            .WithoutAuth()
            .Body(new InviteMemberRequest(email, Role))
            .PostAsync("/test-support/tenants/{tenant}/members", new { tenant = organization.Tenant });

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var member = response.ReadAsJson<TestMemberResponse>()
            ?? throw new InvalidOperationException("The sample app provisioned no member.");
        context.SetContext(new NorthstarMemberContext(
            member.Membership.Id,
            member.Membership.Email,
            member.Membership.Role,
            member.Token));
    }
}
