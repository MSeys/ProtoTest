namespace Northstar.ProtoTest;

using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
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

/// <summary>
/// The Northstar member a demo journey acts as: the isolated tenant plus the authenticator that
/// carries the member's token through REST, GraphQL and gRPC. The identity itself stays a separate
/// <c>[SignedInAs]</c> declaration, because the role a test needs varies per test; this composite
/// declares the part every journey shares. Provisioning a tenant is real work, so
/// <see cref="NorthstarTenantAttribute"/> stays a plain <see cref="ProtoAttribute"/> that this
/// attribute groups.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class NorthstarMemberAttribute(string planId = PlanIds.Free) : ProtoCompositeAttribute
{
    /// <summary>The plan the member's tenant is provisioned on.</summary>
    public string PlanId { get; } = planId;

    protected override IReadOnlyList<Attribute> Compose() =>
    [
        new NorthstarTenantAttribute(PlanId),
        new AuthAttribute<NorthstarAuthenticator>(),
    ];
}

