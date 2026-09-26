namespace ProtoTest.AspNetCore.Web.Tests;

using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.Web;
using ProtoTest.Web.Playwright;

/// <summary>
/// A real browser journey against the application the suite hosts in this process on its own loopback
/// listener, published as the application's address: the browser session, the REST client and the
/// readiness probe all resolve that one address. Chromium is required; where none is installed the
/// attribute skips with its reason instead of failing.
/// </summary>
[Application("Api")]
[RequiresPlaywrightBrowser]
public sealed class InProcessBrowserJourneyTests
{
    [ProtoTest]
    public async Task The_browser_journeys_against_the_application_hosted_in_process()
    {
        var page = Proto.Context.Web().Page<WelcomePage>();

        await page.OpenAsync("/welcome");
        await page.Heading.Should.BeVisibleAsync();
        await page.Heading.Should.HaveTextAsync("Welcome");

        // The application set this cookie on a real response and the browser sent it back on the next
        // navigation, so the journey really crossed the loopback listener.
        await page.OpenAsync("/cookies/set");
        await page.OpenAsync("/cookies/read");
        await page.Body.Should.HaveTextAsync("chocolate");

        // The REST client resolves the same published address.
        using var response = await Proto.Context.Rest().GetAsync("/ping");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);

        // The readiness probe followed that address too, instead of skipping as "in-process".
        var readiness = Setup.Host.Trace.Snapshot().Entities!.Single(
            entity => entity.Id == $"readiness:application:{Setup.LoopbackApplicationName}");
        Assert.That(
            readiness.State["readiness.url"],
            Does.StartWith("http://127.0.0.1:"),
            "the probe waited on the published loopback address");
    }

    public sealed class WelcomePage : WebPage
    {
        public WebElement Heading => Element(By.Role(WebRole.Heading, "Welcome"));

        public WebElement Body => Element(By.Css("body"));
    }
}
