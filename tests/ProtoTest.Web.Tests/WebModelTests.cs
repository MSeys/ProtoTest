namespace ProtoTest.Web.Tests;

using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;
using ProtoTest.Core;
using ProtoTest.Web.Internal;
using ProtoTest.Web.Selenium;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class WebModelTests
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
                Does.Contain("self::table").And.Contain("@role='grid'"));
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
            </body></html>
            """);

        var rows = document.XPathSelectElements(XPath(By.Role(WebRole.Row)));
        var tables = document.XPathSelectElements(XPath(By.Role(WebRole.Table)));
        var lists = document.XPathSelectElements(XPath(By.Role(WebRole.List)));
        var options = document.XPathSelectElements(XPath(By.Role(WebRole.Option)));

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "tr", "div" }),
                "the plain <tr> and the explicit ARIA row both match");
            Assert.That(tables.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "table", "div" }));
            Assert.That(lists.Select(element => element.Name.LocalName), Is.EqualTo(new[] { "ul" }));
            Assert.That(options.Select(element => element.Value), Is.EqualTo(new[] { "en" }));
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
        var context = await host.StartTestAsync("web test", TestMethod());
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
        var context = await host.StartTestAsync("web test", TestMethod());
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
        var context = await host.StartTestAsync("web test", TestMethod());
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
        var context = await host.StartTestAsync("web test", TestMethod());
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
        var context = await host.StartTestAsync("web nesting scope", TestMethod());
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

    [Test]
    public async Task CollectionsAndTables_ShouldSupportLazyZeroAndOneBasedAddressing()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.CountResult = 4;
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
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
        var context = await host.StartTestAsync("web driver scoped cell", TestMethod());
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
    public async Task FormOperations_ShouldUseTheSharedOperationPipeline()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;

        await form.RememberMe.CheckAsync();
        await form.RememberMe.UncheckAsync();
        await form.Language.SelectOptionAsync("nl");
        await form.Password.PressAsync(WebKey.Enter);

        Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(
            new[] { "check:True", "check:False", "select", "press" }));
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries.Select(item => item.Kind),
            Has.Some.EqualTo("web.select_option"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Assertions_ShouldPollAndKeepFormValuesOutOfTrace()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("loading");
        factory.Backend.TextResults.Enqueue("ready");
        factory.Backend.ValueResult = "super-secret";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;

        await form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await form.Password.Should.HaveValueAsync("super-secret", TimeSpan.FromSeconds(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var serializedTrace = System.Text.Json.JsonSerializer.Serialize(host.Trace.Snapshot());
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Count(item => item.Kind == "text"), Is.EqualTo(2));
            Assert.That(serializedTrace, Does.Not.Contain("super-secret"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries.Count(item => item.Kind == "assert.web"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task NegatedAssertions_ShouldPassWhenTheCheckDoesNotHold()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.VisibleResult = false;
        factory.Backend.EnabledResult = false;
        factory.Backend.CheckedResult = false;
        factory.Backend.TextResults.Enqueue("draft");
        factory.Backend.ValueResult = "old";
        factory.Backend.CurrentAddress = "https://example.test/login";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated assertions", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;

        await form.Status.ShouldNot.BeVisibleAsync(TimeSpan.FromSeconds(1));
        await form.Submit.ShouldNot.BeEnabledAsync(TimeSpan.FromSeconds(1));
        await form.RememberMe.ShouldNot.BeCheckedAsync(TimeSpan.FromSeconds(1));
        await form.Status.ShouldNot.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await form.Password.ShouldNot.HaveValueAsync("new", TimeSpan.FromSeconds(1));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var assertions = host.Trace.Snapshot().Tests.Single().Entries
            .Where(item => item.Kind == "assert.web")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(assertions, Has.Length.EqualTo(5), "one assertion entry per negated assertion");
            Assert.That(assertions.Select(item => item.Attributes["web.assert.negated"]), Is.All.EqualTo("true"));
            Assert.That(assertions.Select(item => item.Attributes["web.expectation"]), Is.EqualTo(new[]
            {
                "not be visible",
                "not be enabled",
                "not be checked",
                "not have text \"ready\"",
                "not have the expected value"
            }));
            Assert.That(context.RecordedObservations.Count(item => item.Kind == "web.page.verified"),
                Is.EqualTo(5), "a passing negated assertion still verifies the page");
        });
    }

    [Test]
    public async Task NegatedAssertion_ShouldFailWhenTheCheckKeepsHolding()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated timeout", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;

        var exception = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await form.Status.ShouldNot.BeVisibleAsync(TimeSpan.FromMilliseconds(150)));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        var assertion = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "assert.web");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("should not be visible"));
            Assert.That(exception.Message, Does.Contain("within 00:00:00.1500000"),
                "the negated assertion honours its timeout");
            Assert.That(exception.Message, Does.Contain("Last observed: visible"));
            Assert.That(assertion.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(assertion.Attributes["web.assert.negated"], Is.EqualTo("true"));
            Assert.That(assertion.Attributes["web.expectation"], Is.EqualTo("not be visible"));
        });
    }

    [Test]
    public async Task NegatedTextAssertion_ShouldFailWhenTheTextMatches()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextDefault = "ready";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web negated text", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;

        var exception = Assert.ThrowsAsync<WebAssertionException>(async () =>
            await form.Status.ShouldNot.HaveTextAsync("ready", TimeSpan.FromMilliseconds(150)));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("should not have text \"ready\""));
            Assert.That(exception.Message, Does.Contain("text was \"ready\""));
        });
    }

    [Test]
    public async Task Navigate_ShouldRecordTheVisitedPagePathAfterRedirects()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.NavigateAddressOverride = "https://example.test/dashboard?tab=orders#top";
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web page visited", TestMethod());

        await context.Web().Page<LoginPage>().OpenAsync("https://example.test/login?token=secret#form");

        var visited = context.RecordedObservations.Where(item => item.Kind == "web.page.visited").ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(visited.Select(item => item.Identifier), Is.EqualTo(new[] { "/dashboard" }),
                "the final address wins and only the path is recorded");
            Assert.That(visited.Single().TargetName, Is.EqualTo("Web"));
            Assert.That(visited.Single().Metadata!["web.session"], Is.EqualTo("Default"));
        });
    }

    [Test]
    public async Task Assertion_ShouldRecordTheVerifiedPagePath()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("ready");
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web page verified", TestMethod());
        var page = context.Web().Page<LoginPage>();

        await page.OpenAsync("https://example.test/login");
        await page.Form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));

        var verified = context.RecordedObservations.Where(item => item.Kind == "web.page.verified").ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(verified.Select(item => item.Identifier), Is.EqualTo(new[] { "/login" }));
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.visited"), Is.True);
        });
    }

    [Test]
    public async Task WebCoverage_ShouldReportVisitedVerifiedAndInventoriedPages()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.TextResults.Enqueue("ready");
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Pages:0"] = "/inventory",
                ["ProtoTest:Web:Pages:1"] = "dashboard"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web coverage", TestMethod());
        var session = context.Web();

        await session.Page<LoginPage>().OpenAsync("https://example.test/login");
        factory.Backend.CurrentAddress = "https://example.test/dashboard";
        await session.Page<LoginPage>().Form.Status.Should.HaveTextAsync("ready", TimeSpan.FromSeconds(1));
        context.RecordObservation("Web", "web.page.available", "/new-page");

        var items = context.Services.GetServices<IProtoCollector>()
            .OfType<WebCoverageCollector>()
            .Single()
            .GetReportItems()
            .ToDictionary(item => item.Identifier, StringComparer.Ordinal);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(items.Keys, Is.EquivalentTo(new[] { "/login", "/dashboard", "/new-page", "/inventory" }));
            Assert.That(items["/dashboard"].IsCovered, Is.True, "a verified page is covered");
            Assert.That(items["/dashboard"].Count, Is.EqualTo(1), "count is the number of verifications");
            Assert.That(items["/dashboard"].DisplayName, Is.EqualTo("/dashboard"));
            Assert.That(items["/login"].IsCovered, Is.False, "a visited-only page is uncovered");
            Assert.That(items["/new-page"].IsCovered, Is.False, "a discovered-only page is uncovered");
            Assert.That(items["/inventory"].IsCovered, Is.False, "an inventoried-only page is uncovered");
            Assert.That(items.Values.All(item =>
                item.Category == "Web" && item.Kind == ProtoReportItemKinds.Coverage), Is.True);
        });
    }

    [Test]
    public async Task RouteDiscovery_ShouldRecordVueRoutesWhenTheRouterAnswers()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.JsonResult = """["/orders","/orders/new"]""";
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery", TestMethod());

        await context.Web().Page<LoginPage>().OpenAsync("https://example.test/orders");

        var available = context.RecordedObservations
            .Where(item => item.Kind == "web.page.available")
            .Select(item => item.Identifier)
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(available, Is.EquivalentTo(new[] { "/orders", "/orders/new" }));
            Assert.That(factory.Backend.EvaluatedScripts, Has.Some.Contains("__vue_app__"));
        });
    }

    [Test]
    public async Task RouteDiscovery_ShouldBeANoOpWithoutAVueRouter()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue absent", TestMethod());

        await context.Web().Page<LoginPage>().OpenAsync("https://example.test/login");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.available"), Is.False);
            Assert.That(context.RecordedObservations.Any(item => item.Kind == "web.page.visited"), Is.True);
            Assert.That(factory.Backend.EvaluatedScripts, Has.Some.Contains("__vue_app__"));
        });
    }

    [Test]
    public async Task MiddlewareAndNamedWaits_ShouldWrapSelectedOperations()
    {
        var factory = new FakeBackendFactory();
        var middlewareProbe = new MiddlewareProbe();
        var waitProbe = new WaitProbe();
        var host = CreateHost(factory, builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(middlewareProbe);
                services.AddSingleton(waitProbe);
            });
            builder.AddWebMiddleware<RecordingMiddleware>();
            builder.AddWebWait<TwoPassWait>(
                WebWaitTiming.Before,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(1),
                WebOperationKind.Click);
        });
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());

        await context.Web().Page<LoginPage>().Form.Submit.ClickAsync();

        Assert.Multiple(() =>
        {
            Assert.That(middlewareProbe.Events, Is.EqualTo(new[] { "before:Click", "after:Click" }));
            Assert.That(waitProbe.Observations, Is.EqualTo(2));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries, Has.Some.Matches<ProtoTraceEntry>(
                entry => entry.Kind == "web.wait" && entry.Outcome == ProtoTraceOutcome.Succeeded));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Operations_ShouldTraceSemanticsAndRedactFillValues()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
        await context.Web().Page<LoginPage>().Form.Password.FillAsync("super-secret");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var trace = host.Trace.Snapshot().Tests.Single();
        var fill = trace.Entries.Single(entry => entry.Kind == "web.fill");
        var backend = trace.Entries.Single(entry => entry.Kind == "web.backend.execute");
        Assert.Multiple(() =>
        {
            Assert.That(fill.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(fill.Attributes["web.component"], Is.EqualTo("LoginPage.Form"));
            Assert.That(fill.Attributes["web.locator"], Is.EqualTo("Label(\"Password\", exact: true)"));
            Assert.That(fill.Attributes["web.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(fill.Attributes.Values, Has.None.Contains("super-secret"));
            Assert.That(backend.ParentId, Is.EqualTo(fill.Id));
        });
    }

    [Test]
    public async Task FailureCapture_ShouldPreserveOriginalExceptionAndRegisterArtifacts()
    {
        var expected = new InvalidOperationException("native click failed");
        var factory = new FakeBackendFactory { Failure = expected };
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());

        var actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Web().Page<LoginPage>().Form.Submit.ClickAsync());
        Assert.That(actual, Is.SameAs(expected));
        Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("web-failure.txt"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(expected));
        var click = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.click");
        Assert.That(click.Error?.Message, Is.EqualTo("native click failed"));
    }

    [Test]
    public async Task DownloadAsync_ShouldUseTheCapabilityTraceAndAttachTheFile()
    {
        var stub = new DownloadingBackend
        {
            DownloadResult = new WebDownload("orders.csv", "text/csv", Encoding.UTF8.GetBytes("id,total\n1,42"))
        };
        var factory = new FakeBackendFactory(stub);
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web download", TestMethod());
        var triggered = false;
        var timeout = TimeSpan.FromSeconds(3);

        var download = await context.Web().DownloadAsync(
            _ =>
            {
                triggered = true;
                return Task.CompletedTask;
            },
            name: "Monthly orders.csv",
            timeout: timeout);

        var entry = host.Trace.Snapshot().Tests.Single().Entries.Single(item => item.Kind == "web.download");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.True);
            Assert.That(stub.TriggerCount, Is.EqualTo(1));
            Assert.That(stub.LastTimeout, Is.EqualTo(timeout));
            Assert.That(download.FileName, Is.EqualTo("Monthly orders.csv"));
            Assert.That(download.MediaType, Is.EqualTo("text/csv"), "an explicit name with the same extension keeps the type");
            Assert.That(download.Size, Is.EqualTo(13));
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(entry.Attributes["web.download.name"], Is.EqualTo("Monthly orders.csv"));
            Assert.That(entry.Attributes["web.download.media_type"], Is.EqualTo("text/csv"));
            Assert.That(entry.Attributes["web.download.size"], Is.EqualTo("13"));
            Assert.That(entry.Attributes["web.download.requested_name"], Is.EqualTo("Monthly orders.csv"));
            Assert.That(entry.Attributes["web.download.timeout"], Is.EqualTo(timeout.ToString()));
            Assert.That(context.Attachments.Select(item => item.Name),
                Has.Some.EndsWith("web-default-download-1-monthly-orders.csv"));
        });
    }

    [Test]
    public async Task DownloadAsync_ShouldFailCleanlyWhenTheBackendHasNoDownloadCapability()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web download unsupported", TestMethod());
        var triggered = false;

        var exception = Assert.ThrowsAsync<WebBackendCapabilityException>(async () =>
            await context.Web().DownloadAsync(_ =>
            {
                triggered = true;
                return Task.CompletedTask;
            }));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.False, "the trigger never runs when the backend cannot capture");
            Assert.That(exception!.Message, Does.Contain("Fake").And.Contains("IWebBackendDownloads"));
        });
    }

    [Test]
    public async Task SeleniumDownload_ShouldFailWithTheDocumentedCapabilityException()
    {
        var host = new ProtoHostBuilder().AddWeb(() => new StubWebDriver()).Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("selenium download", TestMethod());
        var triggered = false;

        var exception = Assert.ThrowsAsync<WebBackendCapabilityException>(async () =>
            await context.Web().DownloadAsync(_ =>
            {
                triggered = true;
                return Task.CompletedTask;
            }));
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(triggered, Is.False, "the trigger never runs on Selenium");
            Assert.That(exception!.Message, Does.Contain("Selenium").And.Contains("WebDriver protocol"));
        });
    }

    [Test]
    public async Task Backend_ShouldBeOwnedByCoreClientLifecycle()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(factory.Backend.Disposed, Is.True);
    }

    [Test]
    public async Task BackendArtifacts_ShouldFinalizeBeforeCorePublishesAttachments()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.AddAttachmentOnComplete = true;
        var publisher = new RecordingAttachmentPublisher();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod(), attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(publisher.Attachments.Select(item => item.Name), Has.Some.EndsWith("native-trace.zip"));
    }

    [Test]
    public async Task SeleniumDiagnostics_ShouldBeRegisteredAsCoreAttachmentBeforePublishing()
    {
        var driver = new StubWebDriver();
        var publisher = new RecordingAttachmentPublisher();
        var host = new ProtoHostBuilder()
            .AddWeb(
                () => driver,
                options => options.DiagnosticTraceRetention = SeleniumDiagnosticTraceRetention.Always)
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod(), attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(publisher.Attachments.Select(item => item.Name), Has.Some.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(driver.QuitCalled, Is.True);
        });
    }

    [Test]
    public async Task SeleniumDiagnostics_ShouldReportAFinalizationFailureWithoutFailingTeardown()
    {
        var driver = new StubWebDriver();
        var host = new ProtoHostBuilder()
            .AddWeb(
                () => driver,
                options => options.DiagnosticTraceRetention = SeleniumDiagnosticTraceRetention.Always)
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod());
        await context.Web().GetBackendAsync<ProtoTest.Web.Selenium.SeleniumWebBackend>();
        driver.ThrowOnUrl = true;

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(context.Attachments.Select(item => item.Name),
                Has.None.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "web.diagnostics.artifact_failed" &&
                    entry.Outcome == ProtoTraceOutcome.Failed));
        });
    }

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
        var context = await host.StartTestAsync("web origin", TestMethod());
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
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Sessions:Default:DiscoverRoutes"] = "true"
            })));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("vue discovery retry", TestMethod());
        var session = context.Web();

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
        var context = await host.StartTestAsync("web repeated failure", TestMethod());
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
        var context = await host.StartTestAsync("web isolated artifacts", TestMethod());
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
        var context = await host.StartTestAsync("web location fallback", TestMethod());
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
        var context = await host.StartTestAsync("web test", TestMethod());
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
            TestMethod(),
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
            TestMethod(),
            [new LoginAsAttribute<ContextAwareLoginStrategy>("Administrator")]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldCreateAndOpenTheDeclaredSessionDuringSetup()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [
                new WebSessionAttribute("Anon"),
                new WebSessionAttribute("Admin") { Open = "https://example.test/dashboard" }
            ]);

        var admin = Proto.Context.Web("Admin");
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Does.Contain("navigate"));
            Assert.That(admin, Is.SameAs(Proto.Context.Web("Admin")));
            Assert.That(Proto.Context.Web("Anon"), Is.Not.SameAs(admin));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldResolveRelativeOpenAgainstItsApplication()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Admin:BaseUrl"] = "https://env.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [new WebSessionAttribute("Admin") { Open = "/back-office" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://env.test/back-office"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldUseTheApplicationNamedOnTheAttribute()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://control.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [new WebSessionAttribute("Admin") { Application = "ControlPlane", Open = "/back-office" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://control.test/back-office"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldUseTheApplicationFromConfiguration()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Sessions:Admin:Application"] = "ControlPlane",
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://control.test"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [new WebSessionAttribute("Admin") { Open = "/back-office" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://control.test/back-office"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldPreferAConfiguredOpenUrlOverTheAttribute()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder => builder.ConfigureAppConfiguration(configuration =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Sessions:Admin:Open"] = "https://env.test/from-config"
            })));
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [new WebSessionAttribute("Admin") { Open = "https://code.test/from-code" }]);

        Assert.That(factory.Backend.Operations.Single().Value, Is.EqualTo("https://env.test/from-config"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task WebSession_ShouldRequireAnOriginForRelativeOpen()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await host.StartTestAsync(
                "web session",
                TestMethod(),
                [new WebSessionAttribute("Admin") { Open = "/back-office" }]));

        Assert.That(exception!.Message, Does.Contain("BaseUrl"));
    }

    [Test]
    public async Task WebSession_ShouldPreferAnOpenUrlFromStartedInfrastructure()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory, builder =>
        {
            builder.AddInfrastructure(new FakeSettingsInfrastructure(new Dictionary<string, string>
            {
                ["ProtoTest:Web:Sessions:Admin:Open"] = "http://standalone.test:8080/from-infrastructure"
            }));
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Sessions:Admin:Open"] = "https://env.test/from-config"
                }));
        });
        await using var ownedHost = host;
        await host.StartAsync();

        await host.StartTestAsync(
            "web session",
            TestMethod(),
            [new WebSessionAttribute("Admin") { Open = "https://code.test/from-code" }]);

        Assert.That(factory.Backend.Operations.Single().Value,
            Is.EqualTo("http://standalone.test:8080/from-infrastructure"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Options_ShouldPreferInfrastructureSettingsOverConfiguration()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:07",
                    ["ProtoTest:Web:Selenium:PollInterval"] = "00:00:00.250"
                }))
            .AddInfrastructure(new FakeSettingsInfrastructure(new Dictionary<string, string>
            {
                ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:09"
            }))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethod());

        var settings = context.TryService<ProtoInfrastructureSettings>();
        var options = WebBackendOptions.Resolve<SeleniumWebOptions>(context, "Default");

        Assert.Multiple(() =>
        {
            Assert.That(settings, Is.Not.Null);
            Assert.That(options.ActionTimeout, Is.EqualTo(TimeSpan.FromSeconds(9)),
                "started infrastructure overrides the static configuration for the keys it provides");
            Assert.That(options.PollInterval, Is.EqualTo(TimeSpan.FromMilliseconds(250)),
                "keys the infrastructure does not provide still come from configuration");
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private sealed class FakeSettingsInfrastructure(IReadOnlyDictionary<string, string> settings)
        : IProtoSettingsInfrastructure
    {
        public string Id => "application:test";
        public string Kind => "application";
        public string Description => "Test settings infrastructure";
        public ProtoResourceScope Scope => ProtoResourceScope.Run;
        public IReadOnlyDictionary<string, string> Settings { get; } = settings;
        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }

    // Compile-time guard: [WebSession] must resolve to WebSessionAttribute even though WebSession is a type.
    [WebSession("Admin", Open = "/dashboard")]
    private sealed class WebSessionAttributeSyntax;

    [Test]
    public async Task Flow_ShouldRunStepsInOrderInsideOneTracedOperation()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethod());
        var form = context.Web().Page<LoginPage>().Form;
        var customStepRan = false;

        await form.Flow("Sign in")
            .Fill(f => f.Password, "super-secret")
            .Check(f => f.RememberMe)
            .Check(f => f.RememberMe, isChecked: false)
            .Select(f => f.Language, "nl")
            .Press(f => f.Password, WebKey.Enter)
            .Do((f, _) =>
            {
                customStepRan = ReferenceEquals(f, form);
                return ValueTask.CompletedTask;
            })
            .Click(f => f.Submit)
            .RunAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var flow = entries.Single(entry => entry.Kind == "web.flow");
        var steps = entries
            .Where(entry => entry.ParentId == flow.Id && entry.Kind != "web.session.initialize")
            .Select(entry => entry.Kind)
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(
                new[] { "fill", "check:True", "check:False", "select", "press", "click" }));
            Assert.That(customStepRan, Is.True);
            Assert.That(flow.Name, Is.EqualTo("WEB flow · Sign in"));
            Assert.That(flow.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(flow.Attributes["web.flow.step_count"], Is.EqualTo("7"));
            Assert.That(steps, Is.EqualTo(new[]
            {
                "web.fill", "web.check", "web.check", "web.select_option", "web.press", "web.click"
            }));
        });
    }

    [Test]
    public async Task Flow_ShouldStopAtTheFailingStepAndFailTheFlowOperation()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethod());
        var expected = new InvalidOperationException("submit is broken");

        var actual = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Web().Page<LoginPage>().Form.Flow("Sign in")
                .Do((_, _) =>
                {
                    factory.Backend.Failure = expected;
                    return ValueTask.CompletedTask;
                })
                .Click(f => f.Submit)
                .Fill(f => f.Password, "never typed")
                .RunAsync());
        await host.CompleteTestAsync(ProtoTestResult.Failed(expected));

        var flow = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.flow");
        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(expected));
            Assert.That(factory.Backend.Operations.Select(item => item.Kind), Is.EqualTo(new[] { "click" }));
            Assert.That(flow.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(flow.Error?.Message, Is.EqualTo("submit is broken"));
        });
    }

    [Test]
    public async Task Flow_ShouldOnlyRunOnce()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web flow", TestMethod());
        var flow = context.Web().Page<LoginPage>().Form.Flow("Submit").Click(f => f.Submit);

        await flow.RunAsync();

        Assert.Multiple(() =>
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await flow.RunAsync());
            Assert.Throws<InvalidOperationException>(() => flow.Click(f => f.Submit));
            Assert.That(factory.Backend.Operations.Count(item => item.Kind == "click"), Is.EqualTo(1));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task InteractAsync_ShouldWrapAnArbitraryInteractionInANamedFlow()
    {
        var factory = new FakeBackendFactory();
        var host = CreateHost(factory);
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web interaction", TestMethod());

        await context.Web().Page<LoginPage>().Form.InteractAsync(
            "Submit twice",
            async form =>
            {
                await form.Submit.ClickAsync();
                await form.Submit.ClickAsync();
            });
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var flow = entries.Single(entry => entry.Kind == "web.flow");
        Assert.Multiple(() =>
        {
            Assert.That(flow.Name, Is.EqualTo("WEB flow · Submit twice"));
            Assert.That(flow.Attributes["web.flow.step_count"], Is.EqualTo("1"));
            Assert.That(entries.Count(entry => entry.Kind == "web.click" && entry.ParentId == flow.Id), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task JQueryIdleWait_ShouldPollUntilJQueryReportsNoActiveRequests()
    {
        var factory = new FakeBackendFactory();
        factory.Backend.EvaluateResults.Enqueue(false);
        factory.Backend.EvaluateResults.Enqueue(false);
        factory.Backend.EvaluateResults.Enqueue(true);
        var host = CreateHost(factory, builder => builder.AddWebWait<JQueryIdleWait>(
            WebWaitTiming.After,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1),
            WebOperationKind.Click));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("jquery wait", TestMethod());

        await context.Web().Page<LoginPage>().Form.Submit.ClickAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var wait = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "web.wait");
        Assert.Multiple(() =>
        {
            Assert.That(factory.Backend.EvaluatedScripts, Has.Count.EqualTo(3));
            Assert.That(factory.Backend.EvaluatedScripts, Has.All.Contains("window.jQuery.active === 0"));
            Assert.That(wait.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(wait.Attributes["web.wait.last_observed"], Is.EqualTo("jQuery is absent or has no active requests"));
        });
    }

    [Test]
    public async Task JQueryIdleWait_ShouldTimeOutWithTheLastObservation()
    {
        var factory = new FakeBackendFactory();
        // The wait polls until its timeout, and how often that is depends on the machine: a queue of
        // answers would run dry and read as ready. jQuery stays busy for as long as this test runs.
        factory.Backend.EvaluateDefault = false;
        var host = CreateHost(factory, builder => builder.AddWebWait<JQueryIdleWait>(
            WebWaitTiming.Before,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(5),
            WebOperationKind.Click));
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("jquery wait", TestMethod());

        var exception = Assert.ThrowsAsync<WebWaitTimeoutException>(async () =>
            await context.Web().Page<LoginPage>().Form.Submit.ClickAsync());
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("jQuery.active is greater than zero"));
            Assert.That(factory.Backend.Operations.Where(item => item.Kind == "click"), Is.Empty);
        });
    }

    [Test]
    public void Locators_ShouldDescribeEveryFactory()
    {
        Assert.Multiple(() =>
        {
            Assert.That(By.Css("form > button.primary").Describe(), Is.EqualTo("Css(\"form > button.primary\")"));
            Assert.That(By.Text("Saved", exact: true, ignoreCase: true).Describe(),
                Is.EqualTo("Text(\"Saved\", exact: true, ignoreCase: true)"));
            Assert.That(By.Placeholder("Search", exact: false).Describe(),
                Is.EqualTo("Placeholder(\"Search\", exact: false)"));
            Assert.That(By.Attribute("data-state", "open").Describe(),
                Is.EqualTo("Attribute(\"data-state\", \"open\")"));
            Assert.That(By.TableCellAt(0).Describe(), Is.EqualTo("TableCellAt(0)"));
        });
    }

    [Test]
    public void Locators_ShouldRejectInvalidArguments()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentException>(() => By.Attribute("data state", "open"));
            Assert.Throws<ArgumentException>(() => By.Attribute("onclick=\"x\"", "open"));
            Assert.Throws<ArgumentException>(() => By.Css(" "));
            Assert.Throws<ArgumentException>(() => By.Text(""));
            Assert.Throws<ArgumentException>(() => By.Placeholder(""));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldTranslateEscapeHatchAndTextLocators()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Css("form > button.primary")),
                Is.EqualTo("By.CssSelector: form > button.primary"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Attribute("data-state", "open")),
                Does.Contain("@data-state='open'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Placeholder("Search")),
                Does.Contain("@placeholder='Search'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Placeholder("Sea", exact: false)),
                Does.Contain("contains(@placeholder,'Sea')"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.Text("Saved", exact: true)),
                Does.Contain("normalize-space(.)='Saved'"));
            Assert.That(SeleniumLocatorTranslator.DiagnosticSelector(By.TableCellAt(0)),
                Does.Contain("position()=1"));
        });
    }

    [Test]
    public void SeleniumTranslator_ShouldExplainUnsupportedCombinations()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.HasText("INV-123")));
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.Css("tr").And(By.HasText("INV-123"))));
            Assert.Throws<WebBackendCapabilityException>(() =>
                SeleniumLocatorTranslator.DiagnosticSelector(By.Role(WebRole.Row).And(By.TestId("invoice"))));
        });
    }

    [Test]
    public async Task SeleniumOptions_ShouldBindBackendAndSessionSectionsFromConfiguration()
    {
        var publisher = new RecordingAttachmentPublisher();
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Web:Selenium:DiagnosticTraceRetention"] = "Always",
                    ["ProtoTest:Web:Sessions:Quiet:DiagnosticTraceRetention"] = "Off"
                }))
            .AddWeb(() => new StubWebDriver())
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web test", TestMethod(), attachmentPublisher: publisher);
        await context.Web().Page<LoginPage>().OpenAsync("https://example.test");
        await context.Web("Quiet").Page<LoginPage>().OpenAsync("https://example.test");

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var names = publisher.Attachments.Select(item => item.Name).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(names, Has.Some.EndsWith("selenium-default-diagnostics.json"));
            Assert.That(names, Has.None.Contains("quiet").IgnoreCase);
        });
    }

    [Test]
    public async Task Options_ShouldApplyCodeThenBackendThenSessionConfiguration()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Playwright:Browser"] = "Firefox",
                ["ProtoTest:Web:Playwright:Channel"] = "msedge",
                ["ProtoTest:Web:Playwright:Context:Locale"] = "nl-BE",
                ["ProtoTest:Web:Playwright:Context:ViewportSize:Width"] = "1280",
                ["ProtoTest:Web:Playwright:Context:ViewportSize:Height"] = "720",
                ["ProtoTest:Web:Sessions:Admin:Channel"] = "chrome-beta",
                ["ProtoTest:Web:Sessions:Admin:TraceRetention"] = "Always"
            }))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethod());

        var resolved = WebBackendOptions.Resolve<ProtoTest.Web.Playwright.PlaywrightWebOptions>(
            context,
            "Admin",
            options =>
            {
                options.Headless = false;
                options.Channel = "chrome";
                options.SlowMo = 10;
            });

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Headless, Is.False, "code value without configuration is kept");
            Assert.That(resolved.SlowMo, Is.EqualTo(10));
            Assert.That(resolved.Browser, Is.EqualTo(ProtoTest.Web.Playwright.PlaywrightBrowser.Firefox));
            Assert.That(resolved.Channel, Is.EqualTo("chrome-beta"), "session section wins over backend section");
            Assert.That(resolved.TraceRetention, Is.EqualTo(ProtoTest.Web.Playwright.PlaywrightTraceRetention.Always));
            Assert.That(resolved.Context.Locale, Is.EqualTo("nl-BE"));
            Assert.That(resolved.Context.ViewportSize?.Width, Is.EqualTo(1280));
            Assert.That(resolved.Context.ViewportSize?.Height, Is.EqualTo(720));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Options_ShouldValidateTheBoundResult()
    {
        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:00"
            }))
            .Build();
        await using var ownedHost = host;
        await host.StartAsync();
        var context = await host.StartTestAsync("web options", TestMethod());

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WebBackendOptions.Resolve<SeleniumWebOptions>(context, "Default", validate: SeleniumWebOptions.Validate),
                "the bound result is validated");
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                WebBackendOptions.Resolve<SeleniumWebOptions>(
                    context,
                    "Default",
                    configure: options => options.PollInterval = TimeSpan.Zero,
                    validate: SeleniumWebOptions.Validate),
                "code configuration that survives binding is validated");
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private sealed class RecordingLoginStrategy : IWebLoginStrategy
    {
        public ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
        {
            Assert.That(context.Persona, Is.EqualTo("Administrator"));
            Assert.That(context.Web.Name, Is.EqualTo("Default"));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ContextAwareLoginStrategy(ProtoExecutionContext constructedWith) : IWebLoginStrategy
    {
        public ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
        {
            Assert.That(context.Execution, Is.SameAs(constructedWith));
            return ValueTask.CompletedTask;
        }
    }

    private static ProtoHost CreateHost(
        FakeBackendFactory factory,
        Action<IProtoHostBuilder>? configure = null)
    {
        var builder = new ProtoHostBuilder();
        builder.AddWebBackend(factory);
        configure?.Invoke(builder);
        return builder.Build();
    }

    private static MethodInfo TestMethod() => typeof(WebModelTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static void Placeholder() { }

    public sealed class InvoicesPage : WebPage
    {
        public InvoiceTable Table => Component<InvoiceTable>(By.TestId("invoice-table"));
    }

    public sealed class InvoiceTable : WebTable<InvoiceRow>
    {
        public InvoiceRow Invoice(string number) => Component<InvoiceRow>(
            By.Role(WebRole.Row).And(By.HasText(number)), nameof(Invoice));
    }

    public sealed class InvoiceRow : WebTableRow
    {
        public WebElement Open => Element(By.Role(WebRole.Link, "Open"));
        public WebElement Number => Cell("Invoice number", name: nameof(Number));
    }

    public sealed class LoginPage : WebPage
    {
        public LoginForm Form => Component<LoginForm>();
    }

    public sealed class LoginForm : WebComponent
    {
        public WebElement Password => Element(By.Label("Password"));
        public WebElement RememberMe => Element(By.Label("Remember me"));
        public WebElement Language => Element(By.Label("Language"));
        public WebElement Status => Element(By.Role(WebRole.Status));
        public WebElement Submit => Element(By.Role(WebRole.Button, "Sign in"));
    }

    private sealed class FakeBackendFactory : IWebBackendFactory
    {
        public FakeBackendFactory(FakeBackend? backend = null) => Backend = backend ?? new FakeBackend();
        public string Name => "Fake";
        public FakeBackend Backend { get; }
        public Exception? Failure { get => Backend.Failure; init => Backend.Failure = value; }
        public ValueTask<IWebBackend> CreateAsync(
            ProtoExecutionContext context,
            string sessionName,
            CancellationToken cancellationToken = default)
        {
            Backend.Context = context;
            return ValueTask.FromResult<IWebBackend>(Backend);
        }
    }

    private class FakeBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics
    {
        public string Name => "Fake";
        public List<(string Kind, WebElementReference? Element, string? Value)> Operations { get; } = [];
        public List<WebBackendOperationContext> BegunOperations { get; } = [];
        public Exception? Failure { get; set; }
        public ProtoExecutionContext? Context { get; set; }
        public bool AddAttachmentOnComplete { get; set; }
        public int CountResult { get; set; }
        public Queue<string> TextResults { get; } = new();

        /// <summary>What a read answers once the queued answers run out. Sticky, so a stable text stays stable however often it is polled.</summary>
        public string TextDefault { get; set; } = "text";
        public string? ValueResult { get; set; }
        public bool VisibleResult { get; set; } = true;
        public bool EnabledResult { get; set; } = true;
        public bool CheckedResult { get; set; } = true;
        public bool Disposed { get; private set; }

        /// <summary>Simulates the browser's final address; a test sets it to model a redirect.</summary>
        public string? NavigateAddressOverride { get; set; }
        public string? CurrentAddress { get; set; }

        public ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
        {
            Operations.Add(("navigate", null, address.ToString()));
            CurrentAddress = NavigateAddressOverride ?? address.ToString();
            return ValueTask.CompletedTask;
        }

        public ValueTask BeginOperationAsync(
            WebBackendOperationContext operation,
            CancellationToken cancellationToken = default)
        {
            BegunOperations.Add(operation);
            return ValueTask.CompletedTask;
        }

        public ValueTask ClickAsync(WebElementReference element, CancellationToken cancellationToken = default)
        {
            Operations.Add(("click", element, null));
            if (Failure is not null) throw Failure;
            return ValueTask.CompletedTask;
        }

        public ValueTask FillAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        {
            Operations.Add(("fill", element, value));
            if (Failure is not null) throw Failure;
            return ValueTask.CompletedTask;
        }

        public ValueTask CheckAsync(WebElementReference element, bool isChecked, CancellationToken cancellationToken = default)
        {
            Operations.Add(($"check:{isChecked}", element, null));
            return ValueTask.CompletedTask;
        }

        public ValueTask SelectOptionAsync(WebElementReference element, string value, CancellationToken cancellationToken = default)
        {
            Operations.Add(("select", element, value));
            return ValueTask.CompletedTask;
        }

        public ValueTask PressAsync(WebElementReference element, WebKey key, CancellationToken cancellationToken = default)
        {
            Operations.Add(("press", element, key.ToString()));
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> CountAsync(WebElementReference elements, CancellationToken cancellationToken = default)
        {
            Operations.Add(("count", elements, null));
            return ValueTask.FromResult(CountResult);
        }

        public ValueTask<string> ReadTextAsync(WebElementReference element, CancellationToken cancellationToken = default)
        {
            Operations.Add(("text", element, null));
            return ValueTask.FromResult(TextResults.Count == 0 ? TextDefault : TextResults.Dequeue());
        }

        public ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
        {
            Operations.Add(("value", element, null));
            return ValueTask.FromResult(ValueResult);
        }

        public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(VisibleResult);

        public ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(EnabledResult);

        public ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(CheckedResult);

        public Queue<bool> EvaluateResults { get; } = new();

        /// <summary>What an evaluation answers once the queued answers run out. Ready, unless a test says otherwise.</summary>
        public bool EvaluateDefault { get; set; } = true;
        public List<string> EvaluatedScripts { get; } = [];
        public string? JsonResult { get; set; }
        public Exception? JsonFailure { get; set; }

        public ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
        {
            EvaluatedScripts.Add(script);
            return ValueTask.FromResult(EvaluateResults.Count == 0 ? EvaluateDefault : EvaluateResults.Dequeue());
        }

        public ValueTask<string?> EvaluateJsonAsync(string script, CancellationToken cancellationToken = default)
        {
            EvaluatedScripts.Add(script);
            if (JsonFailure is { } failure)
            {
                JsonFailure = null;
                throw failure;
            }

            return ValueTask.FromResult(JsonResult);
        }

        public ValueTask<IReadOnlyList<ProtoTestAttachment>> CaptureFailureAsync(WebFailureContext failure, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ProtoTestAttachment>>(
                [ProtoTestAttachment.FromText("web-failure.txt", failure.Exception.Message)]);

        public ValueTask CompleteAsync(CancellationToken cancellationToken = default)
        {
            if (AddAttachmentOnComplete)
                Context!.AddAttachment("native-trace.zip", ReadOnlyMemory<byte>.Empty, "application/zip");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DownloadingBackend : FakeBackend, IWebBackendDownloads
    {
        public WebDownload? DownloadResult { get; set; }
        public TimeSpan? LastTimeout { get; private set; }
        public int TriggerCount { get; private set; }

        public async ValueTask<WebDownload> DownloadAsync(
            Func<CancellationToken, Task> trigger,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            TriggerCount++;
            LastTimeout = timeout;
            await trigger(cancellationToken);
            return DownloadResult ?? throw new InvalidOperationException("No download result is configured.");
        }
    }

    private sealed class RecordingAttachmentPublisher : IProtoTestAttachmentPublisher
    {
        public List<ProtoTestAttachment> Attachments { get; } = [];
        public ValueTask PublishAsync(ProtoTestAttachment attachment, CancellationToken cancellationToken = default)
        {
            Attachments.Add(attachment);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MiddlewareProbe
    {
        public List<string> Events { get; } = [];
    }

    private sealed class RecordingMiddleware(MiddlewareProbe probe) : IWebOperationMiddleware
    {
        public async ValueTask InvokeAsync(
            WebOperationContext context,
            WebOperationDelegate next,
            CancellationToken cancellationToken = default)
        {
            probe.Events.Add($"before:{context.Kind}");
            await next(context, cancellationToken);
            probe.Events.Add($"after:{context.Kind}");
        }
    }

    private sealed class WaitProbe
    {
        public int Observations { get; set; }
    }

    private sealed class TwoPassWait(WaitProbe probe) : IWebWaitCondition
    {
        public string Name => "two-pass readiness";

        public ValueTask<WebWaitObservation> ObserveAsync(
            WebWaitContext context,
            CancellationToken cancellationToken = default)
        {
            probe.Observations++;
            return ValueTask.FromResult(probe.Observations >= 2
                ? WebWaitObservation.Ready("ready")
                : WebWaitObservation.Pending("still loading"));
        }
    }

    private sealed class StubWebDriver : OpenQA.Selenium.IWebDriver, OpenQA.Selenium.ITakesScreenshot
    {
        private string _url = "https://example.test/";

        public bool QuitCalled { get; private set; }
        public bool ThrowOnUrl { get; set; }
        public List<OpenQA.Selenium.By> FindAllQueries { get; } = [];
        public string Url
        {
            get => ThrowOnUrl ? throw new InvalidOperationException("url unavailable") : _url;
            set => _url = value;
        }

        public string Title => "Stub browser";
        public string PageSource => "<html></html>";
        public OpenQA.Selenium.Screenshot GetScreenshot() => new(Convert.ToBase64String([1, 2, 3]));
        public string CurrentWindowHandle => "window";
        public System.Collections.ObjectModel.ReadOnlyCollection<string> WindowHandles => new([CurrentWindowHandle]);
        public void Close() { }
        public void Quit() => QuitCalled = true;
        public OpenQA.Selenium.IWebElement FindElement(OpenQA.Selenium.By by) => throw new OpenQA.Selenium.NoSuchElementException();
        public System.Collections.ObjectModel.ReadOnlyCollection<OpenQA.Selenium.IWebElement> FindElements(OpenQA.Selenium.By by)
        {
            FindAllQueries.Add(by);
            return new([]);
        }
        public OpenQA.Selenium.IOptions Manage() => throw new NotSupportedException();
        public OpenQA.Selenium.INavigation Navigate() => new StubNavigation(this);
        public OpenQA.Selenium.ITargetLocator SwitchTo() => throw new NotSupportedException();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private sealed class StubNavigation(StubWebDriver driver) : OpenQA.Selenium.INavigation
        {
            public void Back() { }
            public Task BackAsync() => Task.CompletedTask;
            public void Forward() { }
            public Task ForwardAsync() => Task.CompletedTask;
            public void GoToUrl(string url) => driver.Url = url;
            public void GoToUrl(Uri url) => driver.Url = url.ToString();
            public Task GoToUrlAsync(string url) { driver.Url = url; return Task.CompletedTask; }
            public Task GoToUrlAsync(Uri url) { driver.Url = url.ToString(); return Task.CompletedTask; }
            public void Refresh() { }
            public Task RefreshAsync() => Task.CompletedTask;
        }
    }
}
