namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;
using global::ProtoTest.Sql;

/// <summary>Arranging through the domain instead of the API, over the same database.</summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
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
            .ReadRequired<ProjectResponse>();

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
}
