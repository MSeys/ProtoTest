namespace Northstar.ProtoTest;

using Microsoft.Extensions.DependencyInjection;
using global::ProtoTest.AspNetCore;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.SampleApp;
using global::ProtoTest.SampleApp.Contracts;
using global::ProtoTest.SampleApp.Domain;

/// <summary>
/// Resolves the Northstar domain a provisioner should write through: the application's own store when
/// it runs in-process - so its events, outbox and audit trail behave exactly as in production - and
/// otherwise the test-side composition over the store the suite shares with the application.
/// </summary>
internal static class NorthstarDomainAccess
{
    public static NorthstarStore Store(ProtoExecutionContext execution)
    {
        if (InProcess(execution))
        {
            return execution.ApplicationServices<Program>(ApplicationName(execution))
                .GetRequiredService<NorthstarStore>();
        }

        return execution.Service<NorthstarStore>();
    }

    public static NorthstarPrincipal Principal(NorthstarStore store, ProtoExecutionContext execution)
    {
        var member = execution.Resolve<NorthstarMemberContext>();
        return store.Authenticate(member.Token);
    }

    public static Uri ApiBaseUrl(ProtoExecutionContext execution)
    {
        var configured = execution.Configuration[$"ProtoTest:Applications:{ApplicationName(execution)}:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return new Uri(configured);
        }

        if (InProcess(execution))
        {
            return execution.ServerFactory<Program>(ApplicationName(execution)).Server.BaseAddress;
        }

        return new Uri("http://localhost");
    }

    private static bool InProcess(ProtoExecutionContext execution)
        => Proto.Host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core");

    private static string ApplicationName(ProtoExecutionContext execution)
        => ProtoApplicationResolution.ResolveState(execution)?.ApplicationName ?? NorthstarTargets.Api;
}

/// <summary>
/// Provisions an isolated organization through <c>NorthstarStore.ProvisionTenant</c> and deletes it
/// again when the test ends. No HTTP call is involved; the application reads the tenant immediately.
/// </summary>
public sealed class NorthstarTenantProvisioner : IProtoDataProvisioner<ProvisionTenantRequest, TenantResponse>
{
    public ValueTask<ProtoDataProvisioningResult<TenantResponse>> CreateAsync(
        ProvisionTenantRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var store = NorthstarDomainAccess.Store(context.Execution);
        var tenant = store.ProvisionTenant(
            value.Name,
            value.PlanId,
            NorthstarDomainAccess.ApiBaseUrl(context.Execution));
        return ValueTask.FromResult(new ProtoDataProvisioningResult<TenantResponse>(
            tenant,
            tenant.Tenant,
            new DeleteTenantOnDispose(store, tenant.Tenant)));
    }

    private sealed class DeleteTenantOnDispose(NorthstarStore store, string slug) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            store.DeleteTenant(slug);
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Creates a project through the domain, as the signed-in member.</summary>
public sealed class NorthstarProjectProvisioner : IProtoDataProvisioner<CreateProjectRequest, ProjectResponse>
{
    public ValueTask<ProtoDataProvisioningResult<ProjectResponse>> CreateAsync(
        CreateProjectRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var store = NorthstarDomainAccess.Store(context.Execution);
        var principal = NorthstarDomainAccess.Principal(store, context.Execution);
        var project = store.CreateProject(principal, value.Name);
        return ValueTask.FromResult(new ProtoDataProvisioningResult<ProjectResponse>(project, project.Id));
    }
}

/// <summary>Creates an environment through the domain, as the signed-in member.</summary>
public sealed class NorthstarEnvironmentProvisioner
    : IProtoDataProvisioner<ProvisionEnvironmentRequest, EnvironmentResponse>
{
    public ValueTask<ProtoDataProvisioningResult<EnvironmentResponse>> CreateAsync(
        ProvisionEnvironmentRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var store = NorthstarDomainAccess.Store(context.Execution);
        var principal = NorthstarDomainAccess.Principal(store, context.Execution);
        var environment = store.CreateEnvironment(principal, value.ProjectId, value.Name, value.Kind);
        return ValueTask.FromResult(new ProtoDataProvisioningResult<EnvironmentResponse>(
            environment,
            environment.Id));
    }
}

/// <summary>
/// Deploys through the domain, as the signed-in member, so the application's own webhook outbox and
/// deployment events see the write.
/// </summary>
public sealed class NorthstarDeploymentProvisioner
    : IProtoDataProvisioner<ProvisionDeploymentRequest, DeploymentResponse>
{
    public ValueTask<ProtoDataProvisioningResult<DeploymentResponse>> CreateAsync(
        ProvisionDeploymentRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var store = NorthstarDomainAccess.Store(context.Execution);
        var principal = NorthstarDomainAccess.Principal(store, context.Execution);
        var deployment = store.Deploy(principal, value.EnvironmentId, value.Version, value.CommitSha);
        return ValueTask.FromResult(new ProtoDataProvisioningResult<DeploymentResponse>(
            deployment,
            deployment.Id));
    }
}

/// <summary>
/// Closes a billing period through the domain: records usage, advances the tenant's clock, and returns
/// the invoice the application issued. The period close runs the same code the application runs.
/// </summary>
public sealed class NorthstarInvoiceProvisioner : IProtoDataProvisioner<IssueInvoiceRequest, InvoiceResponse>
{
    public ValueTask<ProtoDataProvisioningResult<InvoiceResponse>> CreateAsync(
        IssueInvoiceRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var execution = context.Execution;
        var organization = execution.Resolve<NorthstarOrganizationContext>();
        var store = NorthstarDomainAccess.Store(execution);
        var principal = NorthstarDomainAccess.Principal(store, execution);
        store.RecordUsage(principal, value.Metric, value.Quantity);
        store.AdvanceClock(organization.Tenant, TimeSpan.FromDays(value.Days));
        var invoice = store.ListInvoices(principal, InvoiceStatuses.Open, null, 10).Items.FirstOrDefault()
            ?? throw new InvalidOperationException("Closing the billing period produced no open invoice.");
        return ValueTask.FromResult(new ProtoDataProvisioningResult<InvoiceResponse>(invoice, invoice.Number));
    }
}

/// <summary>
/// Creates a member for a role and mints the token the test acts with, through the domain. This is how
/// <c>[SignedInAs]</c> gets a non-owner token when the store is composed.
/// </summary>
public sealed class NorthstarTestMemberProvisioner
    : IProtoDataProvisioner<InviteMemberRequest, TestMemberResponse>
{
    public ValueTask<ProtoDataProvisioningResult<TestMemberResponse>> CreateAsync(
        InviteMemberRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var organization = context.Execution.Resolve<NorthstarOrganizationContext>();
        var store = NorthstarDomainAccess.Store(context.Execution);
        var (membership, token) = store.CreateMemberForRole(organization.Tenant, value.Email, value.Role);
        return ValueTask.FromResult(new ProtoDataProvisioningResult<TestMemberResponse>(
            new TestMemberResponse(membership, token),
            membership.Id));
    }
}
