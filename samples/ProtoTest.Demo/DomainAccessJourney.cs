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
    // Composing the test-side domain needs the store the suite owns; without it the test skips
    // instead of failing, so the same suite runs against an environment it cannot rearrange.
    [RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store, so it cannot compose the domain.")]
    public async Task AProjectProvisionedThroughTheDomainIsVisibleToTheApplication()
    {
        // Act
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "domain-atlas")
            .CreateAsync<ProjectResponse>();

        // Assert: no HTTP call was made to create it, yet the application reads it back.
        using var response = await Proto.Context.Rest()
            .GetAsync("/api/v1/projects/{projectId}", new { projectId = project.Id });
        response.Should.HaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
        {
            id = project.Id,
            name = "domain-atlas",
            status = ProjectStatuses.Active,
            environmentCount = 0
        });
    }
}
