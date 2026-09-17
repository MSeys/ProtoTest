namespace ProtoTest.Web.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;

[TestFixture]
public sealed class PlaywrightConformanceTests
{
    [Test]
    public async Task SharedWebModel_ShouldRunAgainstARealBrowser()
    {
        if (!OperatingSystem.IsWindows() || !EdgeIsInstalled())
            Assert.Ignore("The browser-backed conformance test requires a local Microsoft Edge installation.");

        var host = new ProtoHostBuilder()
            .AddWeb(options =>
            {
                options.Channel = "msedge";
                options.Headless = true;
                options.TraceRetention = PlaywrightTraceRetention.Always;
            })
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("Playwright conformance", TestMethod());
        var web = context.Web();
        var backend = await web.GetBackendAsync<PlaywrightWebBackend>();
        await backend.Page.SetContentAsync(Html);
        var page = web.Page<ConformancePage>();

        await page.Name.FillAsync("Matthias");
        await page.Remember.CheckAsync();
        await page.Language.SelectOptionAsync("nl");
        await page.Save.ShouldBeEnabledAsync(TimeSpan.FromSeconds(2));
        await page.Save.ClickAsync();
        await page.Status.ShouldHaveTextAsync("saved", TimeSpan.FromSeconds(1));
        await page.Invoices.RowNumber(2).Cell("Total").ShouldHaveTextAsync("€ 10");

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
        if (!OperatingSystem.IsWindows() || !EdgeIsInstalled())
            Assert.Ignore("The browser-backed conformance test requires a local Microsoft Edge installation.");

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Playwright:Channel"] = "msedge",
                    ["ProtoTest:Web:Sessions:Default:Context:Locale"] = "nl-BE"
                }))
            .AddWeb(options => options.TraceRetention = PlaywrightTraceRetention.Off)
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("Playwright escape hatches", TestMethod());
        var web = context.Web();
        var backend = await web.GetBackendAsync<PlaywrightWebBackend>();
        await backend.Page.SetContentAsync(EscapeHatchHtml);
        var page = web.Page<EscapeHatchPage>();

        await page.Search.FillAsync("invoice");
        await page.Flow("Subscribe")
            .Check(p => p.Newsletter)
            .Click(p => p.Primary)
            .RunAsync();
        await page.Newsletter.ShouldBeCheckedAsync();
        await page.Banner.ShouldBeVisibleAsync();
        await page.Banner.ShouldContainTextAsync("Subscribed");
        await page.ShoutedBanner.ShouldBeVisibleAsync();
        await page.Invoices.RowMatching(By.HasText("INV-2")).CellAt(1).ShouldHaveTextAsync("€ 20");
        await page.Invoices.RowAt(1).Cell("Total").ShouldHaveTextAsync("€ 10");
        await page.Tags.First().ShouldHaveTextAsync("alpha");
        await page.Tags.Matching(By.HasText("gamma")).ShouldBeVisibleAsync();

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

    private static bool EdgeIsInstalled()
        => File.Exists(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe") ||
           File.Exists(@"C:\Program Files\Microsoft\Edge\Application\msedge.exe");

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
        public ValueTask ShouldHaveTextAsync(string text) => Element(By.Css(":scope")).ShouldHaveTextAsync(text);
        public ValueTask ShouldBeVisibleAsync() => Element(By.Css(":scope")).ShouldBeVisibleAsync();
    }

    private const string EscapeHatchHtml = """
        <!doctype html>
        <html><body>
          <input placeholder="Search invoices">
          <form onsubmit="return false">
            <input type="checkbox" data-field="newsletter" aria-label="Newsletter">
            <button class="primary" onclick="document.getElementById('banner').hidden=false">Go</button>
          </form>
          <p id="banner" hidden>Subscribed to updates</p>
          <a href="#details">Open</a>
          <span data-testid="hidden" style="display:none">secret</span>
          <ul><li class="tag">alpha</li><li class="tag">beta</li><li class="tag">gamma</li></ul>
          <table data-testid="invoices">
            <thead><tr><th>Invoice</th><th>Total</th></tr></thead>
            <tbody><tr><td>INV-1</td><td>€ 10</td></tr><tr><td>INV-2</td><td>€ 20</td></tr></tbody>
          </table>
        </body></html>
        """;

    private const string Html = """
        <!doctype html>
        <html><body>
          <label for="name">Name</label><input id="name">
          <label for="remember">Remember me</label><input id="remember" type="checkbox">
          <label for="language">Language</label><select id="language"><option value="en">English</option><option value="nl">Nederlands</option></select>
          <button disabled onclick="document.querySelector('[role=status]').textContent='saved'">Save</button>
          <div role="status">idle</div>
          <table data-testid="invoices">
            <thead><tr><th>Invoice</th><th>Total</th></tr></thead>
            <tbody><tr><td>INV-1</td><td>€ 10</td></tr></tbody>
          </table>
          <script>setTimeout(() => document.querySelector('button').disabled = false, 100);</script>
        </body></html>
        """;
}
