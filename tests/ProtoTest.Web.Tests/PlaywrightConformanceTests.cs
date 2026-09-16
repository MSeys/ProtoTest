namespace ProtoTest.Web.Tests;

using System.Reflection;
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
            .AddPlaywrightWeb(options =>
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
