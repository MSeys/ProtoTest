namespace ProtoTest.Demo;

using System.Net;
using Northstar.ProtoTest;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.Sql;

/// <summary>Arranging through the domain instead of the API, over the same database.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
public sealed class DomainAccessJourney
{
    [ProtoTest]
    [SignedInAs]
    [RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store, so it cannot inspect it.")]
    public async Task AProjectCreatedThroughRestIsCommittedToTheDatabase()
    {
        const string projectName = "rest-to-store";

        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(projectName))
            .PostAsync("/api/v1/projects");
        var project = created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ReadAsJson<ProjectResponse>()!;

        await using var command = Proto.Context.SqlConnection().CreateCommand();
        command.CommandText = """
            SELECT "Id", "Name", "Status"
            FROM "Projects"
            WHERE "Id" = @id
            """;
        var id = command.CreateParameter();
        id.ParameterName = "@id";
        id.Value = project.Id;
        command.Parameters.Add(id);

        await using var stored = await command.ExecuteReaderAsync();
        Assert.That(await stored.ReadAsync(), Is.True, "The REST write did not create a project row.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(stored.GetString(0), Is.EqualTo(project.Id));
            Assert.That(stored.GetString(1), Is.EqualTo(projectName));
            Assert.That(stored.GetString(2), Is.EqualTo(ProjectStatuses.Active));
        }
    }

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
