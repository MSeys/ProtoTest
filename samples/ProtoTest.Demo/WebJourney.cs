namespace ProtoTest.Demo;

using System.Net;
using global::NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
using ProtoTest.SampleApp.Testing;
using ProtoTest.Sheets;
using ProtoTest.Web;

/// <summary>
/// The Northstar console end to end in one session: sign in on the console's own sign-in screen, land on
/// the dashboard, create a project and its environment on the screen, deploy and watch the rail change,
/// pay an invoice in billing, and download the monthly report from reports and verify it with Sheets.
/// The journey deliberately ends on the not-found screen without asserting anything there, so page
/// coverage shows a page that was visited but never verified.
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
public sealed class WebJourney
{
    private const string ProjectName = "console-atlas";

    [ProtoTest]
    [SignedInAs]
    public async Task TheConsoleFromSignInToTheMonthlyReport()
    {
        // The login strategy drove /console/signin with the tenant's token; the dashboard is the landing.
        var dashboard = Proto.Context.Web().Page<DashboardPage>();
        await dashboard.Page.Should.BeVisibleAsync(DemoSupport.ConsoleWait);
        await dashboard.Title.Should.HaveTextAsync("Dashboard", DemoSupport.ConsoleWait);
        await dashboard.Plan.Should.HaveTextAsync("Growth", DemoSupport.ConsoleWait);

        // Create the project on the console's projects screen.
        await dashboard.NavProjects.ClickAsync();
        var projects = Proto.Context.Web().Page<ProjectsPage>();
        await projects.Title.Should.HaveTextAsync("Projects", DemoSupport.ConsoleWait);
        await projects.NewProject.ClickAsync();
        await projects.NameInput.FillAsync(ProjectName);
        await projects.Create.ClickAsync();
        var projectRow = projects.Project(ProjectName);
        await projectRow.Link.Should.HaveTextAsync(ProjectName, DemoSupport.ConsoleWait);
        await projectRow.Status.Should.HaveTextAsync("Active", DemoSupport.ConsoleWait);

        // Open the detail screen and create the environment the release lands in.
        await projectRow.Link.ClickAsync();
        var project = Proto.Context.Web().Page<ProjectDetailPage>();
        await project.Name.Should.HaveTextAsync(ProjectName, DemoSupport.ConsoleWait);
        await project.EnvironmentsEmpty.Should.BeVisibleAsync(DemoSupport.ConsoleWait);
        await project.NewEnvironment.ClickAsync();
        await project.EnvironmentNameInput.FillAsync("preview");
        await project.CreateEnvironment.ClickAsync();
        var environment = project.Environment("preview");
        await environment.Name.Should.HaveTextAsync("preview", DemoSupport.ConsoleWait);
        await environment.Version.Should.HaveTextAsync("none", DemoSupport.ConsoleWait);

        // Deploy through the console; the rail shows the release and its status.
        await project.DeployVersion.FillAsync("1.0.0");
        await project.DeployCommit.FillAsync("abc1234");
        await project.DeploySubmit.ClickAsync();
        var deployed = project.Deployment("1.0.0");
        await deployed.Status.Should.HaveTextAsync("Succeeded", DemoSupport.ConsoleWait);
        await environment.Version.Should.HaveTextAsync("1.0.0", DemoSupport.ConsoleWait);

        // The screen follows the application's live updates: the WebSocket subscription, or the visible
        // polling fallback when the socket cannot connect. Either way it settles on live or polling.
        await project.LiveIndicator.Should.BeVisibleAsync(DemoSupport.ConsoleWait);
        await project.LiveIndicator.ShouldNot.HaveTextAsync("Connecting", DemoSupport.LiveUpdateWait);

        // A failing release changes the rail again; the environment keeps the last good version.
        await project.DeployVersion.FillAsync("1.1.0");
        await project.DeployCommit.FillAsync("badc0de");
        await project.DeploySubmit.ClickAsync();
        var failed = project.Deployment("1.1.0");
        await failed.Status.Should.HaveTextAsync("Failed", DemoSupport.ConsoleWait);
        await environment.Version.Should.HaveTextAsync("1.0.0", DemoSupport.ConsoleWait);

        // Close the billing period so an invoice exists, then pay it on the billing screen.
        var invoice = await DemoSupport.IssueInvoiceAsync();
        await project.NavBilling.ClickAsync();
        var billing = Proto.Context.Web().Page<BillingPage>();
        await billing.Title.Should.HaveTextAsync("Billing", DemoSupport.ConsoleWait);
        var invoiceRow = billing.Invoice(invoice.Number);
        await invoiceRow.Status.Should.HaveTextAsync("Open", DemoSupport.ConsoleWait);
        await invoiceRow.Pay.ClickAsync();
        await invoiceRow.Status.Should.HaveTextAsync("Paid", DemoSupport.ConsoleWait);
        await invoiceRow.LastPayment.Should.HaveTextAsync("Succeeded", DemoSupport.ConsoleWait);

        // Download the workbook on the reports screen, then verify the same artifact with Sheets.
        await billing.NavReports.ClickAsync();
        var reports = Proto.Context.Web().Page<ReportsPage>();
        await reports.Title.Should.HaveTextAsync("Reports", DemoSupport.ConsoleWait);
        await reports.Download.ClickAsync();
        await reports.Status.Should.HaveTextAsync("monthly.xlsx downloaded.", DemoSupport.ConsoleWait);

        using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        var report = Proto.Context.Sheets().Open(response).Model<SheetsJourney.ProjectReportRow>();
        report.Verify();
        var reportRow = report.Row(candidate => candidate.Name == ProjectName);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(reportRow.Status, Is.EqualTo(ProjectStatuses.Active));
            Assert.That(reportRow.Environments, Is.EqualTo(1));
        }

        // Deliberately unverified ending: the not-found screen is visited, never asserted, so coverage
        // reports a page the console showed but the suite did not cover.
        await reports.OpenAsync("/console/this-screen-was-never-built");
    }
}
