namespace Northstar.ProtoTest;

using global::ProtoTest.Web;

/// <summary>The bounded waits console journeys give a screen, fact or live update to settle.</summary>
public static class NorthstarConsole
{
    /// <summary>How long a screen or fact may take to appear or change.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    /// <summary>How long a live update may take to reach a console screen.</summary>
    public static readonly TimeSpan LiveUpdateWait = TimeSpan.FromSeconds(20);
}

/// <summary>
/// The console shell every screen renders: brand, session facts, navigation and the toast host.
/// </summary>
public abstract class ConsolePage : WebPage
{
    public WebElement Brand => Element(By.TestId("brand"));

    public WebElement SessionOrganization => Element(By.TestId("session-organization"));

    public WebElement SessionPlan => Element(By.TestId("session-plan"));

    public WebElement SignOut => Element(By.TestId("sign-out"));

    public WebElement NavDashboard => Element(By.TestId("nav-dashboard"));

    public WebElement NavProjects => Element(By.TestId("nav-projects"));

    public WebElement NavBilling => Element(By.TestId("nav-billing"));

    public WebElement NavReports => Element(By.TestId("nav-reports"));

    public WebElement Toast => Element(By.TestId("toast"));
}

/// <summary>The console's sign-in screen, the real exchange of a tenant token for a cookie session.</summary>
public sealed class SignInPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("login-page"));

    public WebElement Token => Element(By.TestId("login-token"));

    public WebElement Submit => Element(By.TestId("login-submit"));

    public WebElement Error => Element(By.TestId("login-error"));
}

public sealed class DashboardPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("dashboard-page"));

    public WebElement Title => Element(By.TestId("dashboard-title"));

    public WebElement Refresh => Element(By.TestId("dashboard-refresh"));

    public WebElement Plan => Element(By.TestId("stat-plan"));

    public WebElement ProjectCount => Element(By.TestId("stat-projects"));

    public WebElement DeployMinutes => Element(By.TestId("stat-deploy-minutes"));

    public WebElement Allowance => Element(By.TestId("allowance"));

    public WebElement AllowanceUsed => Element(By.TestId("allowance-used"));

    public WebElement RecentDeployments => Element(By.TestId("recent-deployments"));

    public WebElement RecentDeploymentsEmpty => Element(By.TestId("recent-deployments-empty"));
}

public sealed class ProjectsPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("projects-page"));

    public WebElement Title => Element(By.TestId("projects-title"));

    public WebElement NewProject => Element(By.TestId("new-project"));

    public WebElement CreateForm => Element(By.TestId("create-project-form"));

    public WebElement NameInput => Element(By.TestId("project-name-input"));

    public WebElement Create => Element(By.TestId("create-project"));

    public WebElement Cancel => Element(By.TestId("cancel-project"));

    public WebElement CreateError => Element(By.TestId("create-project-error"));

    public WebElement Search => Element(By.TestId("project-search"));

    public WebElement Table => Element(By.TestId("projects-table"));

    public WebElement Empty => Element(By.TestId("projects-empty"));

    public WebElement NoMatch => Element(By.TestId("projects-no-match"));

    public WebElement Error => Element(By.TestId("projects-error"));

    public WebComponentCollection<ProjectRow> ProjectRows => Components<ProjectRow>(By.TestId("project-row"));

    public ProjectRow Project(string name) => ProjectRows.Matching(By.HasText(name), $"Project[{name}]");
}

public sealed class ProjectRow : WebComponent
{
    public WebElement Link => Element(By.TestId("project-link"));

    public WebElement Status => Element(By.TestId("project-status"));

    public WebElement Environments => Element(By.TestId("project-environments"));

    public WebElement Created => Element(By.TestId("project-created"));
}

public sealed class ProjectDetailPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("project-detail"));

    public WebElement Name => Element(By.TestId("project-detail-name"));

    public WebElement LiveIndicator => Element(By.TestId("live-indicator"));

    public WebElement LastUpdated => Element(By.TestId("last-updated"));

    public WebElement Refresh => Element(By.TestId("refresh-deployments"));

    public WebElement NewEnvironment => Element(By.TestId("new-environment"));

    public WebElement EnvironmentNameInput => Element(By.TestId("environment-name-input"));

    public WebElement EnvironmentKindInput => Element(By.TestId("environment-kind-input"));

    public WebElement CreateEnvironment => Element(By.TestId("create-environment"));

    public WebElement CancelEnvironment => Element(By.TestId("cancel-environment"));

    public WebElement EnvironmentError => Element(By.TestId("environment-error"));

    public WebElement EnvironmentsEmpty => Element(By.TestId("environments-empty"));

    public WebElement DeployEnvironment => Element(By.TestId("deploy-environment"));

    public WebElement DeployVersion => Element(By.TestId("deploy-version"));

    public WebElement DeployCommit => Element(By.TestId("deploy-commit"));

    public WebElement DeploySubmit => Element(By.TestId("deploy-submit"));

    public WebElement DeployError => Element(By.TestId("deploy-error"));

    public WebElement DeploymentsEmpty => Element(By.TestId("deployments-empty"));

    public WebComponentCollection<EnvironmentItem> EnvironmentItems => Components<EnvironmentItem>(By.TestId("environment"));

    public EnvironmentItem Environment(string name) => EnvironmentItems.Matching(By.HasText(name), $"Environment[{name}]");

    public WebComponentCollection<DeploymentRow> DeploymentRows => Components<DeploymentRow>(By.TestId("deployment-row"));

    public DeploymentRow Deployment(string version) => DeploymentRows.Matching(By.HasText(version), $"Deployment[{version}]");
}

public sealed class EnvironmentItem : WebComponent
{
    public WebElement Name => Element(By.TestId("environment-name"));

    public WebElement Kind => Element(By.TestId("environment-kind"));

    public WebElement Status => Element(By.TestId("environment-status"));

    public WebElement Version => Element(By.TestId("environment-version"));

    public WebElement Promote => Element(By.TestId("promote"));
}

public sealed class DeploymentRow : WebComponent
{
    public WebElement Version => Element(By.TestId("deployment-version"));

    public WebElement Status => Element(By.TestId("deployment-status"));

    public WebElement Environment => Element(By.TestId("deployment-environment"));

    public WebElement Sha => Element(By.TestId("deployment-sha"));

    public WebElement Time => Element(By.TestId("deployment-time"));

    public WebElement Rollback => Element(By.TestId("rollback"));
}

public sealed class BillingPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("billing-page"));

    public WebElement Title => Element(By.TestId("billing-title"));

    public WebElement Subscription => Element(By.TestId("subscription"));

    public WebElement SubscriptionStatus => Element(By.TestId("subscription-status"));

    public WebElement SubscriptionPlan => Element(By.TestId("subscription-plan"));

    public WebElement InvoicesEmpty => Element(By.TestId("invoices-empty"));

    public WebComponentCollection<InvoiceRow> InvoiceRows => Components<InvoiceRow>(By.TestId("invoice-row"));

    public InvoiceRow Invoice(string number) => InvoiceRows.Matching(By.HasText(number), $"Invoice[{number}]");
}

public sealed class InvoiceRow : WebComponent
{
    public WebElement Number => Element(By.TestId("invoice-number"));

    public WebElement Status => Element(By.TestId("invoice-status"));

    public WebElement Total => Element(By.TestId("invoice-total"));

    public WebElement PayMethod => Element(By.TestId("pay-method"));

    public WebElement Pay => Element(By.TestId("pay"));

    public WebElement LastPayment => Element(By.TestId("invoice-last-payment"));
}

public sealed class ReportsPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("reports-page"));

    public WebElement Title => Element(By.TestId("reports-title"));

    public WebElement Download => Element(By.TestId("report-download"));

    public WebElement HtmlLink => Element(By.TestId("report-html-link"));

    public WebElement Status => Element(By.TestId("report-status"));

    /// <summary>
    /// Runs the screen's download action and captures the workbook the browser saves, so the test
    /// verifies the artifact a user would actually receive.
    /// </summary>
    public ValueTask<WebDownload> DownloadReportAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
        => DownloadAsync(
            async ct => await Download.ClickAsync(ct),
            name: "monthly.xlsx",
            timeout: timeout,
            cancellationToken: cancellationToken);
}

public sealed class NotFoundPage : ConsolePage
{
    public WebElement Page => Element(By.TestId("not-found"));

    public WebElement Home => Element(By.TestId("not-found-home"));
}

/// <summary>Logs the browser in through the console's own sign-in screen with the tenant's token.</summary>
public sealed class NorthstarConsoleLogin : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var organization = context.Execution.Resolve<NorthstarOrganizationContext>();
        var page = context.Web.Page<SignInPage>();
        await page.OpenAsync("/console/signin", cancellationToken);
        await page.Page.Should.BeVisibleAsync(NorthstarConsole.Wait, cancellationToken);
        await page.Flow("Sign in with the tenant token")
            .Fill(signIn => signIn.Token, organization.OwnerToken)
            .Click(signIn => signIn.Submit)
            .RunAsync(cancellationToken);
        // The exchange lands on the dashboard; the shell's session facts prove the cookie session.
        await page.SessionOrganization.Should.BeVisibleAsync(NorthstarConsole.Wait, cancellationToken);
    }
}
