namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>Provisions members through the real <c>POST /api/v1/members</c> endpoint.</summary>
public sealed class NorthstarMemberProvisioner : NorthstarApiProvisioner<InviteMemberRequest, MembershipResponse>
{
    protected override string Url => "/api/v1/members";

    protected override object Body(InviteMemberRequest value) => value;

    protected override string IdOf(MembershipResponse response) => response.Id;
}

/// <summary>Creates a project through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiProjectProvisioner : NorthstarApiProvisioner<CreateProjectRequest, ProjectResponse>
{
    protected override string Url => "/api/v1/projects";

    protected override object Body(CreateProjectRequest value) => value;

    protected override string IdOf(ProjectResponse response) => response.Id;
}

/// <summary>Creates an environment through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiEnvironmentProvisioner
    : NorthstarApiProvisioner<ProvisionEnvironmentRequest, EnvironmentResponse>
{
    protected override string Url => "/api/v1/projects/{projectId}/environments";

    protected override object Body(ProvisionEnvironmentRequest value) => new CreateEnvironmentRequest(value.Name, value.Kind);

    protected override object? RouteValues(ProvisionEnvironmentRequest value) => new { projectId = value.ProjectId };

    protected override string IdOf(EnvironmentResponse response) => response.Id;
}

/// <summary>Deploys through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiDeploymentProvisioner
    : NorthstarApiProvisioner<ProvisionDeploymentRequest, DeploymentResponse>
{
    protected override string Url => "/api/v1/environments/{environmentId}/deployments";

    protected override object Body(ProvisionDeploymentRequest value) => new CreateDeploymentRequest(value.Version, value.CommitSha);

    protected override object? RouteValues(ProvisionDeploymentRequest value) => new { environmentId = value.EnvironmentId };

    protected override string IdOf(DeploymentResponse response) => response.Id;
}

/// <summary>
/// Closes a billing period over the wire when no domain store is reachable: usage through the public
/// API, the virtual clock through the test-support surface, then the open invoice through the API.
/// </summary>
public sealed class NorthstarApiInvoiceProvisioner
    : IProtoDataProvisioner<IssueInvoiceRequest, InvoiceResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<InvoiceResponse>> CreateAsync(
        IssueInvoiceRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var execution = context.Execution;
        await execution.Service<TestSupportProbe>().EnsureAvailableAsync(execution);
        var organization = execution.Resolve<NorthstarOrganizationContext>();

        using var usage = await execution.Rest()
            .Body(new RecordUsageRequest(value.Metric, value.Quantity))
            .PostAsync("/api/v1/usage", ct: cancellationToken);
        usage.Should.HaveHttpStatus(HttpStatusCode.Created);

        using var clock = await execution.Rest()
            .WithoutAuth()
            .Body(new AdvanceClockRequest(value.Days))
            .PostAsync(
                "/test-support/tenants/{tenant}/clock/advance",
                new { tenant = organization.Tenant },
                ct: cancellationToken);
        clock.Should.HaveHttpStatus(HttpStatusCode.OK);

        using var invoices = await execution.Rest()
            .GetAsync("/api/v1/invoices", new { status = InvoiceStatuses.Open }, ct: cancellationToken);
        invoices.Should.HaveHttpStatus(HttpStatusCode.OK);
        var invoice = invoices.ReadAsJson<CursorPage<InvoiceResponse>>()!.Items.FirstOrDefault()
            ?? throw new InvalidOperationException("Closing the billing period produced no open invoice.");
        return new ProtoDataProvisioningResult<InvoiceResponse>(invoice, invoice.Number);
    }
}
