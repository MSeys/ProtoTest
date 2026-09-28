namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.Json;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// The first journey: a project created over REST comes back, an invalid one is refused, a viewer
/// cannot create one and an unknown one is not found. Every test then reads as its own story in the
/// trace.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class ProjectsJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task CreatingAProjectReturnsIt()
    {
        var name = $"atlas-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .Should.MatchShape(new
            {
                id = JsonValue.NotNull(),
                name,
                status = ProjectStatuses.Active,
                environmentCount = 0
            });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task AnEmptyProjectNameIsRefused()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(""))
            .PostAsync("/api/v1/projects");

        response
            .Should.HaveHttpStatus(HttpStatusCode.BadRequest)
            .Should.MatchShape(new { code = ProblemCodes.ValidationFailed });
    }

    [ProtoTest]
    [SignedInAs("viewer", MemberRoles.Viewer)]
    public async Task AViewerCannotCreateProjects()
    {
        using var response = await Proto.Context.Rest()
            .Body(new CreateProjectRequest("viewer-atlas"))
            .PostAsync("/api/v1/projects");

        response
            .Should.HaveHttpStatus(HttpStatusCode.Forbidden)
            .Should.MatchShape(new { code = ProblemCodes.Forbidden });
    }

    [ProtoTest]
    [SignedInAs]
    public async Task AnUnknownProjectIsNotFound()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects/prj_missing");

        response.Should.HaveHttpStatus(HttpStatusCode.NotFound);
    }
}
