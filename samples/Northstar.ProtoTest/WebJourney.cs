namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;
using global::ProtoTest.Web;
using global::ProtoTest.Web.Playwright;

/// <summary>
/// The cross-layer journey: a project created through the in-process API appears on the page served
/// by the loopback instance, a browser signs in with the tenant token and sees it. The page and the
/// API share one store, so the browser exercises the product's real behavior.
/// </summary>
[Application(NorthstarTargets.Web)]
[WebSession("Default")]
[RequiresPlaywrightBrowser]
[NorthstarMember(PlanIds.Growth)]
public sealed class WebJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task AProjectCreatedThroughTheApiAppearsOnThePage()
    {
        var name = $"browser-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest(NorthstarTargets.Api)
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        var signIn = Proto.Context.Web().Page<SignInPage>();
        await signIn.OpenAsync("/login");
        var organization = Proto.Context.Resolve<NorthstarOrganizationContext>();
        await signIn.Flow("Sign in with the tenant token")
            .Fill(page => page.Token, organization.OwnerToken)
            .Click(page => page.Submit)
            .RunAsync();

        var projects = Proto.Context.Web().Page<ProjectsPage>();
        var row = projects.Project(name);
        await row.Status.Should.HaveTextAsync(ProjectStatuses.Active, NorthstarPages.Wait);

        // The search box filters the rows the application rendered.
        await projects.Flow("Filter the project list")
            .Fill(page => page.Search, name)
            .RunAsync();
        await row.Name.Should.HaveTextAsync(name, NorthstarPages.Wait);
    }
}
