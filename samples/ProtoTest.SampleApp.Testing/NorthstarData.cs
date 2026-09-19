namespace ProtoTest.SampleApp.Testing;

using System.Net;
using ProtoTest.Data;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

public sealed class NorthstarDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        data.For<InviteMemberRequest>()
            .Default(
                request => request.Email,
                context => $"member-{context.TestId}-{context.ObjectSequence:D4}@example.test");
    }
}

/// <summary>Provisions members through the real <c>POST /api/v1/members</c> endpoint.</summary>
public sealed class NorthstarMemberProvisioner : IProtoDataProvisioner<InviteMemberRequest, MembershipResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<MembershipResponse>> CreateAsync(
        InviteMemberRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(value)
            .PostAsync("/api/v1/members", ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var member = response.ReadAsJson<MembershipResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned member.");
        return new ProtoDataProvisioningResult<MembershipResponse>(member, member.Id);
    }
}
