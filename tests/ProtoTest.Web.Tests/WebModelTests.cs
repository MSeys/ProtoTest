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

[TestFixture]
public sealed partial class WebModelTests
{
    [Test]
    public void Locator_ShouldKeepSemanticStructure()
    {
        var locator = By.Role(WebRole.Row).And(By.HasText("INV-123", exact: true));

        Assert.Multiple(() =>
        {
            Assert.That(locator, Is.TypeOf<AndWebLocator>());
            Assert.That(locator.Describe(), Is.EqualTo(
                "Role(Row).And(HasText(\"INV-123\", exact: true, ignoreCase: false))"));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldPreserveSemanticRoleAndTextFilter()
    {
        var selector = SeleniumLocatorTranslator.DiagnosticSelector(
            By.Role(WebRole.Row).And(By.HasText("INV-123", exact: true)));

        Assert.Multiple(() =>
        {
            Assert.That(selector, Does.StartWith("By.XPath:"));
            Assert.That(selector, Does.Contain("@role='row'"));
            Assert.That(selector, Does.Contain("normalize-space(.)='INV-123'"));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldResolveImplicitHtmlRoles()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Row)),
                Does.Contain("self::tr").And.Contain("@role='row'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Table)),
                Does.Contain("self::table").And.Contain("@role='table'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Grid)),
                Does.Contain("@role='grid'").And.Not.Contain("self::table"),
                "a plain table is a table, not a grid, on both backends");
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.List)),
                Does.Contain("self::ul").And.Contain("self::ol").And.Contain("@role='list'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.ListItem)),
                Does.Contain("self::li").And.Contain("@role='listitem'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Option)),
                Does.Contain("self::option").And.Contain("@role='option'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Combobox)),
                Does.Contain("self::select").And.Contain("@role='combobox'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.RowGroup)),
                Does.Contain("self::tbody").And.Contain("self::thead").And.Contain("self::tfoot")
                    .And.Contain("@role='rowgroup'"));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldMatchImplicitHtmlRolesAgainstPlainHtml()
    {
        var document = XDocument.Parse(
            """
            <html><body>
              <table><tbody><tr><td>INV-1</td></tr></tbody></table>
              <div role="table"><div role="row">ARIA</div></div>
              <ul><li>alpha</li></ul>
              <select><option>en</option></select>
              <dialog open="open">Dialog</dialog>
              <nav>Nav</nav>
              <progress value="1" max="2"></progress>
              <output>42</output>
              <input type="search" />
              <input type="range" min="0" max="1" />
              <input type="number" />
            </body></html>
            """);

        var rows = document.XPathSelectElements(XPath(By.Role(WebRole.Row)));
        var tables = document.XPathSelectElements(XPath(By.Role(WebRole.Table)));
        var lists = document.XPathSelectElements(XPath(By.Role(WebRole.List)));
        var options = document.XPathSelectElements(XPath(By.Role(WebRole.Option)));
        var dialogs = document.XPathSelectElements(XPath(By.Role(WebRole.Dialog)));
        var navigation = document.XPathSelectElements(XPath(By.Role(WebRole.Navigation)));
        var progress = document.XPathSelectElements(XPath(By.Role(WebRole.ProgressBar)));
        var status = document.XPathSelectElements(XPath(By.Role(WebRole.Status)));
        var searchboxes = document.XPathSelectElements(XPath(By.Role(WebRole.Searchbox)));
        var sliders = document.XPathSelectElements(XPath(By.Role(WebRole.Slider)));
        var spinButtons = document.XPathSelectElements(XPath(By.Role(WebRole.SpinButton)));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "tr", "div" }),
                "the plain <tr> and the explicit ARIA row both match");
            Assert.That(tables.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "table", "div" }));
            Assert.That(lists.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "ul" }));
            Assert.That(options.Select(element => element.Value), Is.EqualTo(new[] { "en" }));
            Assert.That(dialogs.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "dialog" }));
            Assert.That(navigation.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "nav" }));
            Assert.That(progress.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "progress" }));
            Assert.That(status.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "output" }));
            Assert.That(searchboxes.Single().Attribute("type")?.Value, Is.EqualTo("search"));
            Assert.That(sliders.Single().Attribute("type")?.Value, Is.EqualTo("range"));
            Assert.That(spinButtons.Single().Attribute("type")?.Value, Is.EqualTo("number"));
        });
    }

    [Test]
    public void RoleVocabulary_ShouldCoverEveryWebRoleOnBothBackends()
    {
        Assert.Multiple(() =>
        {
            foreach (var role in Enum.GetValues<WebRole>())
            {
                Assert.That(
                    SeleniumLocatorTranslator.DiagnosticSelector(By.Role(role)),
                    Does.Contain($"@role='{WebRoleMap.AriaName(role)}'"),
                    $"Selenium must always accept the explicit ARIA name for {role}");
                Assert.That(
                    PlaywrightWebBackend.MapRole(role).ToString().ToLowerInvariant(),
                    Is.EqualTo(WebRoleMap.AriaName(role)),
                    $"Playwright's native mapping for {role} must be the shared ARIA name");
            }
        });
    }

    private static string XPath(WebLocator locator)
    {
        const string prefix = "By.XPath: ";
        var selector = SeleniumLocatorTranslator.DiagnosticSelector(locator);
        Assert.That(selector, Does.StartWith(prefix));
        return selector[prefix.Length..];
    }

    [Test]
    public async Task Components_ShouldBuildLazyHierarchicalReferences()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var page = context.Web().Page<InvoicesPage>();

        var open = page.Table.Invoice("INV-123").Open;
        Assert.That(factory.Backend.Operations, Is.Empty);
        await open.ClickAsync();

        var operation = factory.Backend.Operations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(operation.Kind, Is.EqualTo("click"));
            Assert.That(operation.Element!.ComponentPath, Is.EqualTo("InvoicesPage.Table.Invoice"));
            Assert.That(operation.Element.ComponentRoots, Has.Count.EqualTo(2));
            Assert.That(operation.Element.ComponentRoots[0], Is.EqualTo(By.TestId("invoice-table")));
            Assert.That(operation.Element.ComponentRoots[1].Describe(), Does.Contain("INV-123"));
            Assert.That(operation.Element.Name, Is.EqualTo(nameof(InvoiceRow.Open)));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Page_ShouldReturnTheSameInstancePerSession()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var session = context.Web();

        var page = session.Page<InvoicesPage>();
        Assert.Multiple(() =>
        {
            Assert.That(session.Page<InvoicesPage>(), Is.SameAs(page));
            Assert.That(page, Is.Not.SameAs(context.Web("Other").Page<InvoicesPage>()));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WaitUntilAsync_ShouldPollUntilTheConditionHolds()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var session = context.Web();

        var attempts = 0;
        await session.WaitUntilAsync(
            async _ =>
            {
                await Task.Yield();
                return Interlocked.Increment(ref attempts) >= 2;
            },
            TimeSpan.FromSeconds(5));

        Assert.That(attempts, Is.GreaterThanOrEqualTo(2));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WaitUntilAsync_ShouldThrowWhenTheTimeoutElapses()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethods.Placeholder);
        var session = context.Web();

        Assert.ThrowsAsync<WebAssertionException>(async () =>
            await session.WaitUntilAsync(_ => ValueTask.FromResult(false), TimeSpan.FromMilliseconds(100)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(new InvalidOperationException("expected timeout")));
    }

    [Test]
    public async Task WaitUntil_ShouldScopeNestingByOperationLineage()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web nesting scope", TestMethods.Placeholder);
        var session = context.Web();
        var page = session.Page<InvoicesPage>();
        var predicateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ValueTask<string>? fireAndForget = null;
        var entered = false;

        var wait = session.WaitUntilAsync(
            async ct =>
            {
                if (!entered)
                {
                    entered = true;
                    // Fire-and-forget: started inside the wait scope, never awaited there.
                    fireAndForget = page.Table.Invoice("INV-1").Open.TextAsync(ct);
                    await page.Table.Invoice("INV-1").Open.Should.BeVisibleAsync(TimeSpan.FromSeconds(1), ct);
                    predicateEntered.TrySetResult();
                    await release.Task;
                }

                return true;
            },
            TimeSpan.FromSeconds(5));

        await predicateEntered.Task;
        // A second top-level operation started synchronously while the wait is still in flight.
        await page.Table.Invoice("INV-2").Open.TextAsync();
        release.TrySetResult();
        await wait;
        await fireAndForget!.Value;
        // Nothing is left behind: a later top-level operation is still top-level.
        await page.Table.Invoice("INV-3").Open.TextAsync();

        var waitOperation = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.wait.until");
        var operations = factory.Backend.BegunOperations;
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(operations.Any(operation =>
                    operation.Kind == WebOperationKind.ReadText
                    && operation.ParentCorrelationId == waitOperation.Id),
                Is.True, "the fire-and-forget read is nested under the wait by lineage");
            Assert.That(operations.Any(operation =>
                    operation.Kind == WebOperationKind.Assert
                    && operation.ParentCorrelationId == waitOperation.Id),
                Is.True, "the assertion inside the predicate is nested under the wait");
            Assert.That(operations.Count(operation =>
                    operation.Kind == WebOperationKind.ReadText && operation.ParentCorrelationId is null),
                Is.EqualTo(2), "the second top-level read and the later read stay top-level");
        });
    }

}
