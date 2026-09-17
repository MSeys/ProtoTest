namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;

/// <summary>Arranging through the domain instead of the API, over the same database.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class DomainAccessJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task AProjectProvisionedThroughTheDomainIsVisibleToTheApplication()
    {
        // Arrange: composing the domain needs the database the application uses.
        if (Proto.Context.TryService<NorthstarStore>() is null)
        {
            Assert.Ignore("Set ConnectionStrings:Northstar so the test can compose the domain.");
        }

        // Act
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "domain-atlas")
            .CreateAsync<ProjectResponse>();

        // Assert: no HTTP call was made to create it, yet the application reads it back.
        using var response = await Proto.Context.Rest()
            .GetAsync("/api/v1/projects/{projectId}", new { projectId = project.Id });
        response.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            id = project.Id,
            name = "domain-atlas",
            status = ProjectStatuses.Active,
            environmentCount = 0
        });
    }
}
