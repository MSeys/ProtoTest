namespace ProtoTest.Web.Tests;

using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Playwright;
using ProtoTest.Web.Selenium;

public sealed partial class WebModelTests
{
    [Test]
    public async Task PageObservations_ShouldIgnorePagesOnAnotherOriginWhenTheSessionHasABaseUrl()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Default:BaseUrl"] = "https://app.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web origin", TestMethods.Placeholder);
        var session = context.Web();

        // The navigation was redirected to the identity provider: not this application's page.
        factory.Backend.NavigateAddressOverride = "https://idp.example.com/login";
        await session.Page<InvoicesPage>().OpenAsync("/home");

        // Back on this application, a passing assertion is coverage.
        factory.Backend.CurrentAddress = "https://app.test/home";
        await session.Page<InvoicesPage>().Table.Invoice("INV-1").Open.Should.BeVisibleAsync(TimeSpan.FromSeconds(1));

        // An assertion on the payment provider's page is not this application's coverage.
        factory.Backend.CurrentAddress = "https://pay.example.com/checkout";
        await session.Page<InvoicesPage>().Table.Invoice("INV-1").Open.Should.BeVisibleAsync(TimeSpan.FromSeconds(1));

        var observations = context.RecordedObservations
            .Where(observation => observation.Kind is "web.page.visited" or "web.page.verified")
            .Select(observation => (observation.Kind, observation.Identifier))
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(observations.Any(item => item.Kind == "web.page.visited" && item.Identifier == "/home"),
                Is.False,
                "a redirected visit stays on the external origin and is not recorded as this application's page");
            Assert.That(observations.Any(item => item.Kind == "web.page.verified" && item.Identifier == "/home"),
                Is.True);
            Assert.That(observations.Length, Is.EqualTo(1),
                "neither the identity provider's nor the payment page's path contributes coverage");
        });
    }

    [Test]
    public async Task VueRouteDiscovery_ShouldRetryAfterAFailedEvaluation()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery retry", TestMethods.Placeholder);
        var session = context.Web(discoverRoutes: true);

        factory.Backend.JsonFailure = new InvalidOperationException("the router is not ready yet");
        await session.Page<InvoicesPage>().OpenAsync("https://example.test/one");
        factory.Backend.JsonResult = """["/orders"]""";
        await session.Page<InvoicesPage>().OpenAsync("https://example.test/two");

        var available = context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(available, Is.EqualTo(new[] { "/orders" }),
                "a failed first discovery does not latch, so the next navigation retries it");
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entries.Any(entry => entry.Kind == "web.page.discovery.failed"),
                Is.True);
        });
    }

    [Test]
    public async Task SeleniumFailureCapture_ShouldKeepEveryArtifactOfARepeatedFailure()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web repeated failure", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();
        driver.Url = "https://example.test/checkout";
        var failure = new WebFailureContext(
            "Click",
            new WebElementReference([], "LoginPage.Form", "Submit", By.Role(WebRole.Button, "Sign in")),
            new InvalidOperationException("boom"));

        var first = await ((IWebBackendDiagnostics)backend).CaptureFailureAsync(failure);
        var second = await ((IWebBackendDiagnostics)backend).CaptureFailureAsync(failure);

        var names = first.Concat(second).Select(item => item.Name).ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Length.EqualTo(6), "both failures' artifacts survive");
            Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count(), Is.EqualTo(6),
                "the per-failure sequence keeps the repeated failure's names distinct");
            Assert.That(names, Has.Some.EqualTo("web-default-submit-1-failure.png"));
            Assert.That(names, Has.Some.EqualTo("web-default-submit-2-failure.png"));
        });
    }

    [Test]
    public async Task WebFailureCapture_ShouldKeepTheRemainingArtifactsWhenOneFailsToRegister()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder()
            .AddWeb(() => driver, options => options.ActionTimeout = TimeSpan.FromMilliseconds(150))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web isolated artifacts", TestMethods.Placeholder);
        // Occupy one artifact name so registering it fails during the failure capture.
        context.AddAttachment("web-default-submit-1-page.html", "occupied");

        var failure = Assert.ThrowsAsync<WebActionabilityException>(async () =>
            await context.Web().Page<LoginPage>().Form.Submit.ClickAsync());

        var names = context.Attachments.Select(item => item.Name).ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Failed(failure!));

        var shortNames = names.Select(name => name.Split('-', 2)[1]).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(shortNames, Is.EqualTo(new[]
            {
                "web-default-submit-1-failure.png",
                "web-default-submit-1-location.txt",
                "web-default-submit-1-page.html"
            }), "the screenshot and location registered even though the occupied page.html did not");
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entries.Any(entry =>
                    entry.Kind == "web.diagnostics.artifact_failed" && entry.Outcome == ProtoTraceOutcome.Failed),
                Is.True);
        });
    }

    [Test]
    public async Task SeleniumFailureCapture_ShouldFallBackToTheRawAddressWhenSanitizingDoesNotApply()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web location fallback", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();
        driver.Url = "about:blank";
        var failure = new WebFailureContext("Click", null, new InvalidOperationException("boom"));

        var attachments = await ((IWebBackendDiagnostics)backend).CaptureFailureAsync(failure);
        var location = attachments.Single(item => item.Name.EndsWith("location.txt"));
        var content = System.Text.Encoding.UTF8.GetString(await location.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(content, Does.Contain("about:blank"));
    }

    [Test]
    public void WebKeyMap_ShouldMapEveryKeyForBothBackends()
    {
        var seleniumKeys = new Dictionary<WebKey, string>
        {
            [WebKey.Enter] = OpenQA.Selenium.Keys.Enter,
            [WebKey.Tab] = OpenQA.Selenium.Keys.Tab,
            [WebKey.Escape] = OpenQA.Selenium.Keys.Escape,
            [WebKey.Space] = OpenQA.Selenium.Keys.Space,
            [WebKey.Backspace] = OpenQA.Selenium.Keys.Backspace,
            [WebKey.Delete] = OpenQA.Selenium.Keys.Delete,
            [WebKey.ArrowUp] = OpenQA.Selenium.Keys.ArrowUp,
            [WebKey.ArrowDown] = OpenQA.Selenium.Keys.ArrowDown,
            [WebKey.ArrowLeft] = OpenQA.Selenium.Keys.ArrowLeft,
            [WebKey.ArrowRight] = OpenQA.Selenium.Keys.ArrowRight,
            [WebKey.Home] = OpenQA.Selenium.Keys.Home,
            [WebKey.End] = OpenQA.Selenium.Keys.End,
            [WebKey.PageUp] = OpenQA.Selenium.Keys.PageUp,
            [WebKey.PageDown] = OpenQA.Selenium.Keys.PageDown
        };

        Assert.Multiple(() =>
        {
            Assert.That(seleniumKeys.Keys, Is.EquivalentTo(Enum.GetValues<WebKey>()), "every semantic key is covered");
            foreach (var (key, selenium) in seleniumKeys)
            {
                var value = WebKeyMap.Get(key);
                Assert.That(value.Playwright, Is.Not.Empty, $"{key} has a Playwright value");
                Assert.That(value.Selenium, Is.EqualTo(selenium), $"{key} keeps its Selenium value");
            }

            Assert.That(WebKeyMap.Get(WebKey.Enter).Playwright, Is.EqualTo("Enter"));
            Assert.That(WebKeyMap.Get(WebKey.Space).Playwright, Is.EqualTo(" "));
        });
    }

    [Test]
    public async Task SeleniumFailureCapture_ShouldNameArtifactsFromTheSessionAndElement()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();
        var failure = new WebFailureContext(
            "Click",
            new WebElementReference([], "LoginPage.Form", "Submit", By.Role(WebRole.Button, "Sign in")),
            new InvalidOperationException("boom"));

        var attachments = await ((IWebBackendDiagnostics)backend).CaptureFailureAsync(failure);

        Assert.Multiple(() =>
        {
            Assert.That(attachments.Select(item => item.Name), Is.EqualTo(new[]
            {
                "web-default-submit-1-failure.png",
                "web-default-submit-1-page.html",
                "web-default-submit-1-location.txt"
            }));
            Assert.That(attachments.Single(item => item.Name.EndsWith("location.txt")).Description,
                Is.EqualTo("Browser location at web operation failure."));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task LoginAs_ShouldConstructTheStrategyAndPassPersonaAndSession()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web login",
            TestMethods.Placeholder,
            [new LoginAsAttribute<RecordingLoginStrategy>("Administrator")]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task LoginAs_ShouldLetTheStrategyRequestProtoExecutionContextDirectly()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web login",
            TestMethods.Placeholder,
            [new LoginAsAttribute<ContextAwareLoginStrategy>("Administrator")]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

}
