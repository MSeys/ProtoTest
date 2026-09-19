namespace ProtoTest.Web.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;

[TestFixture]
public sealed class PlaywrightConformanceTests
{
    [Test]
    public async Task SharedWebModel_ShouldRunAgainstARealBrowser()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Always;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("Playwright conformance", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync(ConformanceMarkup.Html);
        var page = web.Page<ConformancePage>();

        await page.Name.FillAsync("Matthias");
        await page.Remember.CheckAsync();
        await page.Language.SelectOptionAsync("nl");
        await page.Save.Should.BeEnabledAsync(TimeSpan.FromSeconds(2));
        await page.Save.ClickAsync();
        await page.Status.Should.HaveTextAsync("saved", TimeSpan.FromSeconds(1));
        await page.Invoices.RowNumber(2).Cell("Total").Should.HaveTextAsync("€ 10");

        var name = await page.Name.ValueAsync();
        var remembered = await page.Remember.IsCheckedAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo("Matthias"));
            Assert.That(remembered, Is.True);
            Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("playwright-default-trace.zip"));
        });
    }

    [Test]
    public async Task EscapeHatchLocatorsFlowsAndConfiguredContext_ShouldRunAgainstARealBrowser()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Default:Context:Locale"] = "nl-BE"
                }))
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("Playwright escape hatches", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync(ConformanceMarkup.EscapeHatchHtml);
        var page = web.Page<EscapeHatchPage>();

        await page.Search.FillAsync("invoice");
        await page.Flow("Subscribe")
            .Check(p => p.Newsletter)
            .Click(p => p.Primary)
            .RunAsync();
        await page.Newsletter.Should.BeCheckedAsync();
        await page.Banner.Should.BeVisibleAsync();
        await page.Banner.Should.ContainTextAsync("Subscribed");
        await page.ShoutedBanner.Should.BeVisibleAsync();
        await page.Invoices.RowMatching(By.HasText("INV-2")).CellAt(1).Should.HaveTextAsync("€ 20");
        await page.Invoices.RowAt(1).Cell("Total").Should.HaveTextAsync("€ 10");
        await page.Tags.First().HaveTextAsync("alpha");
        await page.Tags.Matching(By.HasText("gamma")).BeVisibleAsync();

        var status = await page.Open.TextAsync();
        var hiddenVisible = await page.Hidden.IsVisibleAsync();
        var locale = await backend.Page.EvaluateAsync<string>("navigator.language");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo("Open"));
            Assert.That(hiddenVisible, Is.False);
            Assert.That(locale, Is.EqualTo("nl-BE"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "web.flow" && entry.Outcome == ProtoTraceOutcome.Succeeded));
        });
    }

    [Test]
    public async Task NamespacedAttributeLocator_ShouldRunAgainstARealBrowser()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("Playwright namespaced attribute", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync(ConformanceMarkup.NamespacedAttributeHtml);
        var page = web.Page<NamespacedAttributePage>();

        await page.Greeting.Should.HaveTextAsync("Hello");

        var text = await page.Greeting.TextAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(text, Is.EqualTo("Hello"));
    }

    [Test]
    public async Task MultipleMatchRead_ShouldThrowTheResolutionExceptionInsteadOfRawPlaywrightFailure()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("playwright strict read", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync(
            "<!doctype html><html><body><p class=\"dup\">one</p><p class=\"dup\">two</p></body></html>");
        var page = web.Page<DuplicatePage>();

        var exception = Assert.ThrowsAsync<WebElementResolutionException>(async () =>
            await page.Duplicate.TextAsync());

        // Polling assertions retry on a resolution failure, so a multiple match ends as the documented
        // assertion timeout instead of a raw PlaywrightException.
        var assertion = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await page.Duplicate.Should.HaveTextAsync("one", TimeSpan.FromMilliseconds(200)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("at most one"));
            Assert.That(assertion!.Message, Does.Contain("Last observed").And.Contain("at most one"));
        });
    }

    [Test]
    public async Task VueRouteDiscovery_ShouldReadRoutesFromARealPage()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
                }))
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery conformance", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.RouteAsync("**/*", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Body = ConformanceMarkup.VueDiscoveryHtml,
            ContentType = "text/html"
        }));

        await web.Page<ConformancePage>().OpenAsync("https://example.test/vue");

        var available = context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(available, Is.EquivalentTo(new[] { "/orders", "/orders/{id}" }),
            "the injected $router.getRoutes table is read and its dynamic segment normalized");
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task WaitUntil_WithAReadOnTheSameSession_ShouldNotDeadlock()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Always;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("playwright nested wait", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync("""
            <!doctype html>
            <html><body>
              <div role="status">pending</div>
              <script>setTimeout(() => document.querySelector('[role=status]').textContent = 'ready', 150);</script>
            </body></html>
            """);
        var page = web.Page<ConformancePage>();

        // The documented WaitUntil-plus-read pattern: the predicate reads an element through the same
        // session whose wait operation is still open.
        await web.WaitUntilAsync(
            async ct => await page.Status.TextAsync(ct) == "ready",
            TimeSpan.FromSeconds(5));

        var text = await page.Status.TextAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("ready"));
            var entries = host.Trace.Snapshot().Tests.Single().Entries;
            Assert.That(entries.Any(entry => entry.Kind == "web.wait.until"
                && entry.Outcome == ProtoTraceOutcome.Succeeded), Is.True);
            Assert.That(entries.Count(entry => entry.Kind == "web.read_text"), Is.GreaterThanOrEqualTo(1),
                "the nested read ran inside the wait");
        });
    }

    [Test]
    [CancelAfter(30_000)]
    public async Task WaitUntil_WithANegatedAssertionOnTheSameSession_ShouldNotDeadlock()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("playwright negated nested wait", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.SetContentAsync("""
            <!doctype html>
            <html><body>
              <div role="status">pending</div>
              <script>setTimeout(() => document.querySelector('[role=status]').textContent = 'ready', 150);</script>
            </body></html>
            """);
        var page = web.Page<ConformancePage>();

        // The documented nested pattern with a negated assertion: ShouldNot polls through the same
        // session whose wait operation is still open, and passes once the text changes.
        await web.WaitUntilAsync(
            async ct =>
            {
                try
                {
                    await page.Status.ShouldNot.HaveTextAsync("pending", TimeSpan.FromMilliseconds(500), ct);
                    return true;
                }
                catch (WebAssertionException)
                {
                    return false;
                }
            },
            TimeSpan.FromSeconds(5));

        await page.Status.Should.HaveTextAsync("ready");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var wait = entries.Single(entry => entry.Kind == "web.wait.until");
        var negated = entries.Single(entry => entry.Kind == "assert.web"
            && entry.Attributes["web.assert.negated"] == "true"
            && entry.Outcome == ProtoTraceOutcome.Succeeded);
        var ancestors = new HashSet<string>();
        for (var current = negated; current.ParentId is { } parentId;)
        {
            ancestors.Add(parentId);
            current = entries.Single(entry => entry.Id == parentId);
        }

        Assert.That(ancestors, Does.Contain(wait.Id),
            "the negated assertion ran nested inside the wait without deadlocking");
    }

    [Test]
    public async Task FailureCapture_ShouldKeepTheLocationArtifactForAboutBlank()
    {
        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("playwright blank location", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        var failure = new WebFailureContext("Click", null, new InvalidOperationException("boom"));

        var attachments = await ((IWebBackendDiagnostics)backend).CaptureFailureAsync(failure);
        var location = attachments.Single(item => item.Name.EndsWith("location.txt", StringComparison.Ordinal));
        var content = System.Text.Encoding.UTF8.GetString(await location.ReadAllBytesAsync());

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(attachments, Has.Count.EqualTo(3));
            Assert.That(content, Does.Contain("about:blank"),
                "the raw address is the fallback when sanitizing does not produce a value");
        });
    }

    [Test]
    public async Task VueRouteDiscovery_ShouldResolveRelativeChildRoutesFromTheVueTwoTable()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
                }))
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue child routes", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.RouteAsync("**/*", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Body = ConformanceMarkup.VueTwoChildRoutesHtml,
            ContentType = "text/html"
        }));

        await web.Page<ConformancePage>().OpenAsync("https://example.test/vue2");

        var available = context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .Select(observation => observation.Identifier)
            .OrderBy(identifier => identifier, StringComparer.Ordinal)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(available, Is.EqualTo(new[] { "/orders", "/orders/new", "/orders/summary", "/orders/{id}" }),
            "children resolve against their parent, an empty-path default child recurses into its children, " +
            "and a top-level relative path is not a page");
    }

    [Test]
    public async Task VueRouteDiscovery_ShouldBeANoOpOnAPageWithoutAVueApp()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
                }))
            .AddWeb(options =>
            {
                options.Headless = true;
                options.InstallBrowsers = true;
                options.TraceRetention = PlaywrightTraceRetention.Off;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery absent", TestMethod());
        var web = context.Web();
        var backend = await OpenBrowserAsync(web);
        await backend.Page.RouteAsync("**/*", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Body = "<!doctype html><html><body><h1>Plain</h1></body></html>",
            ContentType = "text/html"
        }));

        await web.Page<ConformancePage>().OpenAsync("https://example.test/plain");

        var available = context.RecordedObservations
            .Where(observation => observation.Kind == "web.page.available")
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(available, Is.Empty, "a page without a Vue app answers with nothing");
            Assert.That(context.RecordedObservations.Any(observation => observation.Kind == "web.page.visited"),
                Is.True, "the navigation still records its visited page");
        });
    }

    /// <summary>
    /// Opens the browser, skipping with the reason when no browser can run here - a headless CI image
    /// without the browser dependencies should report a skip, not a failure.
    /// </summary>
    private static async Task<PlaywrightWebBackend> OpenBrowserAsync(WebSession web)
    {
        try
        {
            return await web.GetBackendAsync<PlaywrightWebBackend>();
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlaywrightException)
        {
            Assert.Ignore($"No Playwright browser is available: {exception.Message}");
            throw;
        }
    }

    private static MethodInfo TestMethod()
        => typeof(PlaywrightConformanceTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder() { }

    public sealed class ConformancePage : WebPage
    {
        public WebElement Name => Element(By.Label("Name"));
        public WebElement Remember => Element(By.Label("Remember me"));
        public WebElement Language => Element(By.Label("Language"));
        public WebElement Save => Element(By.Role(WebRole.Button, "Save"));
        public WebElement Status => Element(By.Role(WebRole.Status));
        public InvoiceTable Invoices => Component<InvoiceTable>(By.TestId("invoices"));
    }

    public sealed class InvoiceTable : WebTable<InvoiceRow> { }
    public sealed class InvoiceRow : WebTableRow { }

    public sealed class EscapeHatchPage : WebPage
    {
        public WebElement Search => Element(By.Placeholder("Search invoices"));
        public WebElement Newsletter => Element(By.Attribute("data-field", "newsletter"));
        public WebElement Primary => Element(By.Css("form > button.primary"));
        public WebElement Banner => Element(By.Text("Subscribed", exact: false));
        public WebElement ShoutedBanner => Element(By.Text("SUBSCRIBED TO UPDATES", exact: true, ignoreCase: true));
        public WebElement Open => Element(By.Role(WebRole.Link).And(By.HasText("open", ignoreCase: true)));
        public WebElement Hidden => Element(By.TestId("hidden"));
        public WebComponentCollection<Tag> Tags => Components<Tag>(By.Css("li.tag"));
        public InvoiceTable Invoices => Component<InvoiceTable>(By.TestId("invoices"));
    }

    public sealed class Tag : WebComponent
    {
        public ValueTask HaveTextAsync(string text) => Element(By.Css(":scope")).Should.HaveTextAsync(text);
        public ValueTask BeVisibleAsync() => Element(By.Css(":scope")).Should.BeVisibleAsync();
    }

    public sealed class NamespacedAttributePage : WebPage
    {
        public WebElement Greeting => Element(By.Attribute("xml:lang", "en"));
    }

    public sealed class DuplicatePage : WebPage
    {
        public WebElement Duplicate => Element(By.Css("p.dup"));
    }

}
