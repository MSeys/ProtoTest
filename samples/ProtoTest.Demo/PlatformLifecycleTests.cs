namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[Application(SampleAppTargets.Api)]
[SampleEnvironment]
[RestAuth<SampleUserAuthenticator>]
public sealed class PlatformLifecycleTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task AdministratorCanProvisionWorkspaceDeployReleaseAndInspectAuditTrail()
    {
        using var workspaceResponse = await Proto.Context.Rest()
            .Body(new CreateWorkspaceRequest("payments-production", "eu-west", "enterprise"))
            .PostAsync("/api/workspaces");
        workspaceResponse.ShouldHaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            id = JsonValue.NotNull(), name = "payments-production", region = "eu-west", plan = "enterprise"
        });
        var workspace = workspaceResponse.ReadAsJson<WorkspaceResponse>()!;

        using var releaseResponse = await Proto.Context.Rest()
            .Body(new CreateReleaseRequest("2026.09.13", "a94f47c"))
            .PostAsync("/api/workspaces/{workspaceId}/releases", new { workspaceId = workspace.Id });
        releaseResponse.ShouldHaveHttpStatus(HttpStatusCode.Created).ShouldMatchShape(new
        {
            workspaceId = workspace.Id, version = "2026.09.13", commitSha = "a94f47c", status = "deployed"
        });

        using var controlPlane = await Proto.Context.Rest().GetAsync("/api/control-plane");
        controlPlane.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            workspaceCount = 1, releaseCount = 1, openInvoiceCount = 1,
            monthlyRecurringRevenue = JsonValue.GreaterThan(400m)
        });

        using var audit = await Proto.Context.Rest().GetAsync("/api/audit");
        audit.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new[]
        {
            new { action = "workspace.created", resource = JsonValue.NotNull() },
            new { action = "release.deployed", resource = JsonValue.NotNull() }
        });
    }

    [ProtoTest]
    [SampleUser]
    public async Task MemberCanCreateWorkspaceButCannotDeployProductionRelease()
    {
        using var workspaceResponse = await Proto.Context.Rest()
            .Body(new CreateWorkspaceRequest("preview", "us-east", "starter"))
            .PostAsync("/api/workspaces");
        var workspace = workspaceResponse.ShouldHaveHttpStatus(HttpStatusCode.Created).ReadAsJson<WorkspaceResponse>()!;

        using var releaseResponse = await Proto.Context.Rest()
            .Body(new CreateReleaseRequest("1.0.0", "b3153aa"))
            .PostAsync("/api/workspaces/{workspaceId}/releases", new { workspaceId = workspace.Id });
        releaseResponse.ShouldHaveHttpStatus(HttpStatusCode.Forbidden)
            .ShouldMatchShape(new { error = "insufficient-permissions" });
    }
}
