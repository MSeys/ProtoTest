namespace Northstar.ProtoTest;

using global::ProtoTest.Web;

/// <summary>The bounded wait the page journeys give a screen or fact to settle.</summary>
public static class NorthstarPages
{
    /// <summary>How long a screen or fact may take to appear or change.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);
}

/// <summary>The application's sign-in screen: the real exchange of a tenant token for a cookie session.</summary>
public sealed class SignInPage : WebPage
{
    public WebElement Token => Element(By.TestId("token"));

    public WebElement Submit => Element(By.TestId("login"));

    public WebElement Error => Element(By.TestId("error"));
}

/// <summary>The projects page the server-rendered UI answers with.</summary>
public sealed class ProjectsPage : WebPage
{
    public WebElement Table => Element(By.TestId("projects"));

    public WebElement Search => Element(By.TestId("search"));

    public WebComponentCollection<ProjectRow> Rows => Components<ProjectRow>(By.TestId("project"));

    public ProjectRow Project(string name) => Rows.Matching(By.HasText(name), $"Project[{name}]");
}

/// <summary>One project row on the projects page.</summary>
public sealed class ProjectRow : WebComponent
{
    public WebElement Name => Element(By.TestId("project-name"));

    public WebElement Status => Element(By.TestId("project-status"));

    public WebElement Environments => Element(By.TestId("project-environments"));
}
