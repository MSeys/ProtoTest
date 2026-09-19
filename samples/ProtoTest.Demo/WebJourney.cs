namespace ProtoTest.Demo;

using System.IO;
using System.Net;
using global::NUnit.Framework;
using Northstar.ProtoTest;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Http;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Domain;
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
        await dashboard.Page.Should.BeVisibleAsync(NorthstarConsole.Wait);
        await dashboard.Title.Should.HaveTextAsync("Dashboard", NorthstarConsole.Wait);
        await dashboard.Plan.Should.HaveTextAsync("Growth", NorthstarConsole.Wait);

        // Create the project on the console's projects screen, as one named flow.
        await dashboard.NavProjects.ClickAsync();
        var projects = Proto.Context.Web().Page<ProjectsPage>();
        await projects.Title.Should.HaveTextAsync("Projects", NorthstarConsole.Wait);
        await projects.Flow("Create project")
            .Click(page => page.NewProject)
            .Fill(page => page.NameInput, ProjectName)
            .Click(page => page.Create)
            .RunAsync();
        var projectRow = projects.Project(ProjectName);
        await projectRow.Link.Should.HaveTextAsync(ProjectName, NorthstarConsole.Wait);
        await projectRow.Status.Should.HaveTextAsync("Active", NorthstarConsole.Wait);

        // Open the detail screen and create the environment the release lands in.
        await projectRow.Link.ClickAsync();
        var project = Proto.Context.Web().Page<ProjectDetailPage>();
        await project.Name.Should.HaveTextAsync(ProjectName, NorthstarConsole.Wait);
        await project.EnvironmentsEmpty.Should.BeVisibleAsync(NorthstarConsole.Wait);
        await project.Flow("Create the preview environment")
            .Click(page => page.NewEnvironment)
            .Fill(page => page.EnvironmentNameInput, "preview")
            .Click(page => page.CreateEnvironment)
            .RunAsync();
        var environment = project.Environment("preview");
        await environment.Name.Should.HaveTextAsync("preview", NorthstarConsole.Wait);
        await environment.Version.Should.HaveTextAsync("none", NorthstarConsole.Wait);

        // Deploy through the console; the rail shows the release and its status.
        await project.DeployVersion.FillAsync("1.0.0");
        await project.DeployCommit.FillAsync("abc1234");
        await project.DeploySubmit.ClickAsync();
        var deployed = project.Deployment("1.0.0");
        await deployed.Status.Should.HaveTextAsync("Succeeded", NorthstarConsole.Wait);
        await environment.Version.Should.HaveTextAsync("1.0.0", NorthstarConsole.Wait);

        // The screen follows the application's live updates: the WebSocket subscription, or the visible
        // polling fallback when the socket cannot connect. Either way it settles on live or polling.
        await project.LiveIndicator.Should.BeVisibleAsync(NorthstarConsole.Wait);
        await project.LiveIndicator.ShouldNot.HaveTextAsync("Connecting", NorthstarConsole.LiveUpdateWait);

        // A failing release changes the rail again; the environment keeps the last good version.
        await project.DeployVersion.FillAsync("1.1.0");
        await project.DeployCommit.FillAsync("badc0de");
        await project.DeploySubmit.ClickAsync();
        var failed = project.Deployment("1.1.0");
        await failed.Status.Should.HaveTextAsync("Failed", NorthstarConsole.Wait);
        await environment.Version.Should.HaveTextAsync("1.0.0", NorthstarConsole.Wait);

        // Close the billing period so an invoice exists, then pay it on the billing screen.
        var invoice = await Proto.Context.Data().IssueInvoiceAsync();
        await project.NavBilling.ClickAsync();
        var billing = Proto.Context.Web().Page<BillingPage>();
        await billing.Title.Should.HaveTextAsync("Billing", NorthstarConsole.Wait);
        var invoiceRow = billing.Invoice(invoice.Number);
        await invoiceRow.Status.Should.HaveTextAsync("Open", NorthstarConsole.Wait);
        await invoiceRow.Pay.ClickAsync();
        await invoiceRow.Status.Should.HaveTextAsync("Paid", NorthstarConsole.Wait);
        await invoiceRow.LastPayment.Should.HaveTextAsync("Succeeded", NorthstarConsole.Wait);

        // Download the workbook on the reports screen and verify the browser's own artifact with Sheets.
        await billing.NavReports.ClickAsync();
        var reports = Proto.Context.Web().Page<ReportsPage>();
        await reports.Title.Should.HaveTextAsync("Reports", NorthstarConsole.Wait);

        // Playwright captures the file the browser saves; a backend without the download capability
        // keeps the REST fallback, so the same workbook is still verified from its bytes.
        ProtoWorkbook report;
        try
        {
            var download = await reports.DownloadReportAsync(NorthstarConsole.Wait);
            Assert.That(download.FileName, Is.EqualTo("monthly.xlsx"));
            report = Proto.Context.Sheets().Open(new MemoryStream(download.Content.ToArray()), download.FileName);
        }
        catch (WebBackendCapabilityException)
        {
            await reports.Download.ClickAsync();
            using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
            response.Should.HaveHttpStatus(HttpStatusCode.OK);
            report = Proto.Context.Sheets().Open(response);
        }

        await reports.Status.Should.HaveTextAsync("monthly.xlsx downloaded.", NorthstarConsole.Wait);
        var reportModel = report.Model<SheetsJourney.ProjectReportRow>();
        reportModel.Verify();
        var reportRow = reportModel.Row(candidate => candidate.Name == ProjectName);
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
