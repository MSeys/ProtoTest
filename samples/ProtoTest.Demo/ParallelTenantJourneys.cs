namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[Application(SampleAppTargets.Api)]
[SampleEnvironment]
[RestAuth<SampleUserAuthenticator>]
public sealed class ParallelTenantJourneys
{
    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    [TestCase("eu-north", "enterprise")]
    [TestCase("us-west", "growth")]
    [TestCase("ap-south", "starter")]
    public async Task IndependentOrganizationsRunConcurrently(string region, string plan)
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateWorkspaceRequest($"workspace-{region}", region, plan))
            .PostAsync("/api/workspaces");
        response.ShouldHaveHttpStatus(HttpStatusCode.Created);
        await Task.Delay(120);
        Proto.Context.RecordObservation("ControlPlane", "parallel.tenant", region, new { plan });
    }
}
