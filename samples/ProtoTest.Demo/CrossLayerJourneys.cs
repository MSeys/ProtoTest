namespace ProtoTest.Demo;

using System.Net;
using global::NUnit.Framework;
using Northstar.ProtoTest;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.GraphQL;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.Web;

/// <summary>
/// The API-first half of the cross-layer showcase: a project created through REST is absent from the
/// console until it refreshes, then the screen renders what the API wrote.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
[LoginAs<NorthstarConsoleLogin>("owner")]
[RequiresCapability(
    ProtoCapabilityKinds.Server,
    CapabilityName = "Northstar standalone",
    Reason = "The standalone application is only started when the suite owns the store.")]
[RequiresConsoleBuild]
public sealed class ApiThenBrowserJourney
{
    private const string ProjectName = "api-first";

    [ProtoTest]
    [SignedInAs]
    public async Task AProjectCreatedThroughTheApiAppearsInTheConsole()
    {
        // The console shows an empty organization before the API write.
        var projects = Proto.Context.Web().Page<ProjectsPage>();
        await projects.OpenAsync("/console/projects");
        await projects.Empty.Should.BeVisibleAsync(NorthstarConsole.Wait);

        var project = await Proto.Context.Data().CreateProjectAsync(ProjectName);

        // Refresh: the console re-reads the list the API just changed.
        await projects.OpenAsync("/console/projects");
        var row = projects.Project(ProjectName);
        await row.Link.Should.HaveTextAsync(project.Name, NorthstarConsole.Wait);
        await row.Status.Should.HaveTextAsync("Active", NorthstarConsole.Wait);
    }
}

/// <summary>
/// The browser-first half of the cross-layer showcase: a project created on the console's form is
/// asserted back through REST and GraphQL, so the UI write is proven on the protocol side.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
[LoginAs<NorthstarConsoleLogin>("owner")]
[RequiresCapability(
    ProtoCapabilityKinds.Server,
    CapabilityName = "Northstar standalone",
    Reason = "The standalone application is only started when the suite owns the store.")]
[RequiresConsoleBuild]
public sealed class BrowserThenApiJourney
{
    private const string ProjectName = "browser-first";

    [ProtoTest]
    [SignedInAs]
    public async Task AProjectCreatedInTheConsoleIsVisibleToRestAndGraphQL()
    {
        // Create the project through the console's own form.
        var projects = Proto.Context.Web().Page<ProjectsPage>();
        await projects.OpenAsync("/console/projects");
        await projects.NewProject.ClickAsync();
        await projects.NameInput.FillAsync(ProjectName);
        await projects.Create.ClickAsync();
        await projects.Project(ProjectName).Status.Should.HaveTextAsync("Active", NorthstarConsole.Wait);

        // REST sees the console's write.
        using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var created = response.ReadAsJson<CursorPage<ProjectResponse>>()!.Items
            .SingleOrDefault(candidate => candidate.Name == ProjectName);
        Assert.That(created, Is.Not.Null, $"REST did not return the project '{ProjectName}'.");
        Assert.That(created!.Status, Is.EqualTo(ProjectStatuses.Active));

        // GraphQL sees it too, through the same query the dashboard uses.
        using var graph = await Proto.Context.GraphQL()
            .Query("projects", new { first = 10 })
            .ExpectAsync(new
            {
                totalCount = 1,
                nodes = new[] { new { name = ProjectName, status = ProjectStatuses.Active } }
            });
        graph.ShouldHaveNoErrors();
    }
}
