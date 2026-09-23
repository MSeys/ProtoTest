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
    public async Task CollectionsAndTables_ShouldSupportLazyZeroAndOneBasedAddressing()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.CountResult = 4;
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var table = context.Web().Page<InvoicesPage>().Table;

        var thirdCellOnSecondRow = table.RowNumber(2).CellNumber(3);
        Assert.That(factory.Backend.Operations, Is.Empty);
        Assert.That(await table.Rows.CountAsync(), Is.EqualTo(4));
        await thirdCellOnSecondRow.ClickAsync();

        var click = factory.Backend.Operations.Single(item => item.Kind == "click").Element!;
        Assert.Multiple(() =>
        {
            Assert.That(click.ComponentPath, Is.EqualTo("InvoicesPage.Table.Rows[2]"));
            Assert.That(click.ComponentRoots[^1], Is.EqualTo(By.At(By.Role(WebRole.Row), 1)));
            Assert.That(click.Locator, Is.EqualTo(By.TableCellNumber(3)));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public void Tables_ShouldSupportHeaderAwareCellsForLegacyMarkup()
    {
        var locator = By.TableCell("Invoice number");
        var selector = SeleniumLocatorTranslator.DiagnosticSelector(locator);

        Assert.Multiple(() =>
        {
            Assert.That(locator.Describe(), Is.EqualTo("TableCell(\"Invoice number\", exact: true, ignoreCase: false)"));
            Assert.That(selector, Does.Contain("ancestor::table[1]"));
            Assert.That(selector, Does.Contain("preceding-sibling"));
        });
    }

    [Test]
    public async Task SeleniumTableCellAt_ShouldSearchTheDocumentWhenTheScopeIsTheDriver()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web driver scoped cell", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();

        await backend.CountAsync(new WebElementReference([], "Page", "Table", By.TestId("invoices")));
        await backend.CountAsync(new WebElementReference([], "Page", "Cell", By.TableCellAt(0)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(driver.FindAllQueries[0].ToString(),
                Is.EqualTo("By.XPath: .//*[@data-testid='invoices']"));
            Assert.That(driver.FindAllQueries[1].ToString(),
                Does.StartWith("By.XPath: (//*[self::th or self::td])[1]"),
                "a driver-rooted cell lookup cannot use ./* and widens to the document");
        });
    }

    [Test]
    public async Task SeleniumNestedAt_ShouldApplyEveryIndexInOrder()
    {
        var first = new FakeElement("first");
        var second = new FakeElement("second");
        var driver = new StubWebDriver
        {
            Elements = by => by.ToString().Contains("data-testid='row'", StringComparison.Ordinal)
                ? [first, second]
                : []
        };
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web nested at", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();

        var text = await backend.ReadTextAsync(
            new WebElementReference([], "Page", "Row", By.At(By.At(By.TestId("row"), 1), 0)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("second"),
                "the inner At(1) picks the second match and the outer At(0) picks it from the one-element source");
            Assert.That(driver.FindAllQueries, Has.Count.EqualTo(1),
                "the source is queried once and both indexes apply to the same result");
        });
    }

    [Test]
    public async Task SeleniumNestedAt_WithAnOutOfRangeIndex_ShouldCountZero()
    {
        var driver = new StubWebDriver { Elements = _ => [new FakeElement("only")] };
        var host = new ProtoHostBuilder().AddWeb(() => driver).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web nested at count", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();

        var count = await backend.CountAsync(
            new WebElementReference([], "Page", "Row", By.At(By.At(By.TestId("row"), 0), 1)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(count, Is.EqualTo(0), "the outer index addresses the one-element result of the inner index");
    }

    [Test]
    public void SeleniumTranslator_ShouldIndexAtInsideXPathCompositions()
    {
        var document = XDocument.Parse(
            """
            <html><body>
              <div data-testid="row">first</div>
              <div data-testid="row">second</div>
            </body></html>
            """);

        var locator = By.At(By.TestId("row"), 1).And(By.HasText("second", exact: true));
        var selector = SeleniumLocatorTranslator.DiagnosticSelector(locator);
        var elements = document.XPathSelectElements(XPath(locator));

        Assert.Multiple(() =>
        {
            Assert.That(selector, Does.Contain("(.//*[@data-testid='row'])[2]"));
            Assert.That(elements.Single().Value, Is.EqualTo("second"));
        });
    }

    [Test]
    public async Task SeleniumReadMissing_ShouldThrowTheSharedResolutionException()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder()
            .AddWeb(() => driver, options => options.ActionTimeout = TimeSpan.FromMilliseconds(150))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web read missing", TestMethods.Placeholder);
        var backend = await context.Web().GetBackendAsync<SeleniumWebBackend>();

        var failure = Assert.ThrowsAsync<WebElementResolutionException>(async () =>
            await backend.ReadTextAsync(new WebElementReference([], "Page", "Missing", By.TestId("missing"))));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(failure!.Message, Does.Contain("was not present"),
            "a read of an element that never appears reports the same failure Playwright does");
    }

}
