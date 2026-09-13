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

        using var workspaces = await Proto.Context.GraphQL()
            .Query("workspaces")
            .ExpectAsync(new[]
            {
                new { name = "analytics", region = "eu-central", plan = "growth" }
            });
        workspaces.ShouldHaveNoErrors();

        using var controlPlane = await Proto.Context.GraphQL()
            .Query("controlPlane")
            .ExpectAsync(new
            {
                userCount = 1,
                workspaceCount = 1,
                releaseCount = 0,
                monthlyRecurringRevenue = 199m
            });
        controlPlane.ShouldHaveNoErrors();
    }

    [ProtoTest]
    [SampleUser]
    public async Task AnonymousGraphQLRequestExplainsAuthenticationFailure()
    {
        using var response = await Proto.Context.GraphQL()
            .WithoutAuth()
            .Query("me")
            .Select(new { id = Gql.Field })
            .ExecuteAsync();
        response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
    }
}
