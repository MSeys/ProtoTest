namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Data;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// Provisions members through the real <c>POST /api/v1/members</c> endpoint. This is the portable
/// example: it needs nothing but the public API, so it runs in every environment.
/// </summary>
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

/// <summary>Creates a project through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiProjectProvisioner
    : IProtoDataProvisioner<CreateProjectRequest, ProjectResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<ProjectResponse>> CreateAsync(
        CreateProjectRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(value)
            .PostAsync("/api/v1/projects", ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var project = response.ReadAsJson<ProjectResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned project.");
        return new ProtoDataProvisioningResult<ProjectResponse>(project, project.Id);
    }
}

/// <summary>Creates an environment through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiEnvironmentProvisioner
    : IProtoDataProvisioner<ProvisionEnvironmentRequest, EnvironmentResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<EnvironmentResponse>> CreateAsync(
        ProvisionEnvironmentRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(new CreateEnvironmentRequest(value.Name, value.Kind))
            .PostAsync(
                "/api/v1/projects/{projectId}/environments",
                new { projectId = value.ProjectId },
                ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var environment = response.ReadAsJson<EnvironmentResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned environment.");
        return new ProtoDataProvisioningResult<EnvironmentResponse>(environment, environment.Id);
    }
}

/// <summary>Deploys through the public API when no domain store is reachable.</summary>
public sealed class NorthstarApiDeploymentProvisioner
    : IProtoDataProvisioner<ProvisionDeploymentRequest, DeploymentResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<DeploymentResponse>> CreateAsync(
        ProvisionDeploymentRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(new CreateDeploymentRequest(value.Version, value.CommitSha))
            .PostAsync(
                "/api/v1/environments/{environmentId}/deployments",
                new { environmentId = value.EnvironmentId },
                ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var deployment = response.ReadAsJson<DeploymentResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned deployment.");
        return new ProtoDataProvisioningResult<DeploymentResponse>(deployment, deployment.Id);
    }
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
        await TestSupportProbe.EnsureAvailableAsync(execution);
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
