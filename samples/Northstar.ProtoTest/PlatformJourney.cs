namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.GraphQL;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>The same platform seen through two protocols: a REST write is visible to GraphQL.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class PlatformJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task RestWritesAreVisibleThroughGraphQL()
    {
        // Arrange: write through the public REST surface.
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("atlas"))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Act
        using var projects = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = "atlas", status = ProjectStatuses.Active } }
            });

        // Assert
        projects.Should.HaveNoErrors();
    }
}
