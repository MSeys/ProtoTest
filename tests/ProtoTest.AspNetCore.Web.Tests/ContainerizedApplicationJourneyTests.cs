namespace ProtoTest.AspNetCore.Web.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// Audit 5 A5.7d: the application under test runs in its own container image, started as run
/// infrastructure; the mapped address it serves on is published as the application's <c>BaseUrl</c>,
/// so the REST client, the browser session and the readiness probe all resolve that one container. A
/// machine without a container runtime skips with the reason instead of failing; a machine without a
/// Playwright browser skips the same way.
/// </summary>
[Application(Setup.ContainerApplicationName)]
[RequiresPlaywrightBrowser]
public sealed class ContainerizedApplicationJourneyTests
{
    [ProtoTest]
    public async Task The_browser_and_REST_journey_against_the_containerized_application()
    {
        RequireContainer();

        // REST against the mapped address: the request leaves this process and reaches the image's
        // own Kestrel, which answers with its page.
        using var response = await Proto.Context.Rest().GetAsync("/");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(
            response.Content,
            Does.Contain("Welcome to .NET"),
            "the response came from the containerized application");

        // A real browser journey against the same published address.
        var page = Proto.Context.Web().Page<ContainerPage>();
        await page.OpenAsync("/");
        await page.Heading.Should.BeVisibleAsync();
        await page.Heading.Should.HaveTextAsync("Welcome to .NET");

        // The HTTP readiness probe followed the published mapped address instead of claiming the
        // application is in-process.
        var readiness = Setup.Host.Trace.Snapshot().Entities!
            .Single(entity => entity.Id == $"readiness:application:{Setup.ContainerApplicationName}");
        Assert.That(
            readiness.State["readiness.url"],
            Does.StartWith(Setup.ContainerApplication!.ConnectionString),
            "the probe waited on the mapped container address");

        // The application is not in-process: the host advertises no in-process server for it.
        Assert.That(
            Setup.Host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"),
            Is.False,
            "a containerized application must not advertise an in-process server capability");
    }

    public sealed class ContainerPage : WebPage
    {
        public WebElement Heading => Element(By.Role(WebRole.Heading, "Welcome to .NET"));
    }

    private static void RequireContainer()
    {
        if (Setup.ContainerApplication is null)
        {
            Assert.Ignore($"No container runtime is available for '{Setup.ContainerImage}'. {Setup.ContainerError}");
        }
    }
}

/// <summary>
/// A5.7d: because the application is served from its container rather than the test host, the suite
/// advertises no in-process server, and a <c>[RequiresInProcess]</c> test skips with its reason before
/// its body runs. The body throws, so a regression that starts advertising in-process services fails
/// the run instead of passing quietly.
/// </summary>
public sealed class ContainerizedApplicationSkipTests
{
    [ProtoTest]
    [RequiresInProcess]
    public void RequiresInProcess_ShouldSkipWithItsReason()
        => throw new InvalidOperationException("A skipped test must not run its body.");
}
