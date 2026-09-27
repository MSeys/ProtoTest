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
            .With(request => request.Name, context.UniqueName("northstar"))
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

