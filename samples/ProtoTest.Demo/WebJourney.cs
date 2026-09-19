namespace ProtoTest.Demo;

using global::NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Web;

/// <summary>
/// A real browser journey: the suite's standalone instance of the sample application is started as
/// infrastructure, the tenant's token is exchanged for a cookie on the application's login page, and the
/// projects page renders what the API created.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarTenant(PlanIds.Growth)]
[Auth<NorthstarAuthenticator>]
[LoginAs<NorthstarUiLogin>("owner")]
[RequiresCapability(
    ProtoCapabilityKinds.Server,
    CapabilityName = "Northstar standalone",
    Reason = "The standalone application is only started when the suite owns the store.")]
public sealed class WebJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task AProjectCreatedThroughTheApiIsVisibleInTheBrowser()
    {
        // Arrange through the API.
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, "web-atlas")
            .CreateAsync<ProjectResponse>();

        // Act: open the application's projects page (the setup logged the browser in).
        var page = Proto.Context.Web().Page<ProjectsPage>();
        await page.OpenAsync("/projects");
        await page.Search.FillAsync("web-atlas");

        // Assert: the page renders the project the API created.
        var row = page.Projects.RowMatching(By.HasText("web-atlas"));
        Assert.Multiple(async () =>
        {
            await row.Cell("Project").Should.HaveTextAsync("web-atlas");
            await row.Cell("Status").Should.HaveTextAsync(project.Status);
        });
    }

    public sealed class ProjectsPage : WebPage
    {
        public WebElement Search => Element(By.TestId("search"));

        public ProjectTable Projects => Component<ProjectTable>(By.TestId("projects"));
    }

    public sealed class ProjectTable : WebTable<ProjectRow>;

    public sealed class ProjectRow : WebTableRow;
}
