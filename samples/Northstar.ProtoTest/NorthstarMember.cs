namespace Northstar.ProtoTest;

using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// Resolves the member the test acts as from the identity <c>[SignedInAs]</c> declared: the tenant's
/// owner when the test named no role, otherwise a member invited with the first declared role. The
/// member is provisioned once per test and kept as context state, so the authenticator and the domain
/// provisioners agree on whose token every write carries.
/// </summary>
public static class NorthstarMember
{
    /// <summary>Returns this test's member, provisioning it on first use.</summary>
    public static async ValueTask<NorthstarMemberContext> EnsureAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.TryResolve<NorthstarMemberContext>() is { } existing)
        {
            return existing;
        }

        var user = context.SignedInUser();
        var organization = context.Resolve<NorthstarOrganizationContext>();
        var role = user.Roles.Count > 0 ? user.Roles[0] : MemberRoles.Owner;
        var member = role == MemberRoles.Owner
            ? new NorthstarMemberContext("owner", organization.OwnerEmail, role, organization.OwnerToken)
            : await InviteAsync(context, role);
        context.SetContext(member);
        return member;
    }

    private static async ValueTask<NorthstarMemberContext> InviteAsync(
        ProtoExecutionContext context,
        string role)
    {
        // The role and the test id keep the email stable for a test that provisions twice and unique
        // across parallel tests; the app's member surface is the authority on whether it already exists.
        var member = await context.Data()
            .For<InviteMemberRequest>()
            .With(request => request.Email, $"{role}.{context.TestId}@example.test")
            .With(request => request.Role, role)
            .CreateAsync<TestMemberResponse>();
        return new NorthstarMemberContext(
            member.Membership.Id,
            member.Membership.Email,
            member.Membership.Role,
            member.Token);
    }
}
