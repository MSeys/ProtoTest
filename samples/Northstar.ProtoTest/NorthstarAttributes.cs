namespace Northstar.ProtoTest;

using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// Provisions an isolated Northstar organization and removes it after the test. Whether that runs
/// through the domain or the test-support surface is the registration's decision; the test only sees
/// the provisioned <see cref="NorthstarOrganizationContext"/>.
/// </summary>
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
        var tenant = await context.Data()
            .For<ProvisionTenantRequest>()
            .With(request => request.Name, $"northstar-{context.TestId}")
            .With(request => request.PlanId, PlanId)
            .CreateAsync<TenantResponse>();
        context.SetContext(new NorthstarOrganizationContext(
            tenant.Tenant,
            tenant.OrganizationId,
            tenant.OwnerEmail,
            tenant.OwnerToken,
            tenant.ApiBaseUrl));
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

        var member = await context.Data()
            .For<InviteMemberRequest>()
            .With(request => request.Email, $"{Role}.{context.TestId}@example.test")
            .With(request => request.Role, Role)
            .CreateAsync<TestMemberResponse>();
        context.SetContext(new NorthstarMemberContext(
            member.Membership.Id,
            member.Membership.Email,
            member.Membership.Role,
            member.Token));
    }
}
