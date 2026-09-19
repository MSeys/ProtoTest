namespace Northstar.ProtoTest;

using global::ProtoTest.Data;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>The defaults Northstar fixtures share: a real plan and a unique, readable member email.</summary>
public sealed class NorthstarDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        // A generated string would otherwise take the place of the record's Free default.
        data.For<ProvisionTenantRequest>()
            .Default(request => request.PlanId, PlanIds.Free);
        data.For<InviteMemberRequest>()
            .Default(
                request => request.Email,
                context => $"member-{context.TestId}-{context.ObjectSequence:D4}@example.test");
    }
}
