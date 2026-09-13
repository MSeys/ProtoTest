namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[RestClient(SampleAppTargets.Api)]
[GraphQLClient(SampleAppTargets.GraphQL)]
[SampleEnvironment]
[Auth<SampleUserAuthenticator>]
[GraphQLAuth<SampleGraphQLAuthenticator>]
public sealed class GraphQLControlPlaneTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task RestProvisioningIsImmediatelyVisibleThroughGraphQLControlPlane()
    {
        using var provisioned = await Proto.Context.Rest()
            .Body(new CreateWorkspaceRequest("analytics", "eu-central", "growth"))
            .PostAsync("/api/workspaces");
        provisioned.ShouldHaveStatus(HttpStatusCode.Created);

        using var response = await Proto.Context.GraphQL()
            .Query("ControlPlane", query => query
                .Field("workspaces", workspaces => workspaces.Fields("id", "name", "region", "plan"))
                .Field("controlPlane", dashboard => dashboard.Fields(
                    "tenant", "userCount", "workspaceCount", "releaseCount", "openInvoiceCount", "monthlyRecurringRevenue")))
            .ExecuteAsync();
        response.ShouldHaveNoErrors().ShouldMatchData(new
        {
            workspaces = new[] { new { name = "analytics", region = "eu-central", plan = "growth" } },
            controlPlane = new { userCount = 1, workspaceCount = 1, releaseCount = 0, monthlyRecurringRevenue = 199m }
        });
    }

    [ProtoTest]
    [SampleUser]
    public async Task AnonymousGraphQLRequestExplainsAuthenticationFailure()
    {
        using var response = await Proto.Context.GraphQL()
            .WithoutAuth()
            .Query("Me", query => query.Field("me", me => me.Fields("id")))
            .ExecuteAsync();
        response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
    }
}
