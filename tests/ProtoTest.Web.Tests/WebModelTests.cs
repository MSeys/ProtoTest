namespace ProtoTest.Web.Tests;

using System.Reflection;
using ProtoTest.Core;
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

        await form.Status.ShouldHaveTextAsync("ready", TimeSpan.FromSeconds(1));
        await form.Password.ShouldHaveValueAsync("super-secret", TimeSpan.FromSeconds(1));
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
            .AddSeleniumWeb(
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
        for (var i = 0; i < 1_000; i++) factory.Backend.EvaluateResults.Enqueue(false);
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
            .AddSeleniumWeb(() => new StubWebDriver())
            .AddSeleniumWeb(() => new StubWebDriver(), name: "Quiet")
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
    public void OptionsBinder_ShouldApplyCodeThenBackendThenSessionConfiguration()
    {
        var options = new ProtoTest.Web.Playwright.PlaywrightWebOptions
        {
            Headless = false,
            Channel = "chrome",
            SlowMo = 10
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Web:Playwright:Browser"] = "Firefox",
            ["ProtoTest:Web:Playwright:Channel"] = "msedge",
            ["ProtoTest:Web:Playwright:Context:Locale"] = "nl-BE",
            ["ProtoTest:Web:Playwright:Context:ViewportSize:Width"] = "1280",
            ["ProtoTest:Web:Playwright:Context:ViewportSize:Height"] = "720",
            ["ProtoTest:Web:Sessions:Admin:Channel"] = "chrome-beta",
            ["ProtoTest:Web:Sessions:Admin:TraceRetention"] = "Always"
        }).Build();
        var binder = new WebBackendOptionsBinder<ProtoTest.Web.Playwright.PlaywrightWebOptions>(
            options, "Playwright", "Admin");

        var resolved = binder.Resolve(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(options));
            Assert.That(resolved.Headless, Is.False, "code value without configuration is kept");
            Assert.That(resolved.SlowMo, Is.EqualTo(10));
            Assert.That(resolved.Browser, Is.EqualTo(ProtoTest.Web.Playwright.PlaywrightBrowser.Firefox));
            Assert.That(resolved.Channel, Is.EqualTo("chrome-beta"), "session section wins over backend section");
            Assert.That(resolved.TraceRetention, Is.EqualTo(ProtoTest.Web.Playwright.PlaywrightTraceRetention.Always));
            Assert.That(resolved.Context.Locale, Is.EqualTo("nl-BE"));
            Assert.That(resolved.Context.ViewportSize?.Width, Is.EqualTo(1280));
            Assert.That(resolved.Context.ViewportSize?.Height, Is.EqualTo(720));
        });
    }

    [Test]
    public void OptionsBinder_ShouldBindOnceAndValidateTheBoundResult()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:00"
        }).Build();
        var invalid = new WebBackendOptionsBinder<SeleniumWebOptions>(
            new SeleniumWebOptions(), "Selenium", "Default", SeleniumWebOptions.Validate);

        Assert.Throws<ArgumentOutOfRangeException>(() => invalid.Resolve(configuration));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebBackendOptionsBinder<SeleniumWebOptions>(
            new SeleniumWebOptions { PollInterval = TimeSpan.Zero }, "Selenium", "Default", SeleniumWebOptions.Validate));

        var options = new SeleniumWebOptions();
        var binder = new WebBackendOptionsBinder<SeleniumWebOptions>(options, "Selenium", "Default");
        var first = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:07"
        }).Build();
        var second = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ProtoTest:Web:Selenium:ActionTimeout"] = "00:00:09"
        }).Build();

        binder.Resolve(first);
        binder.Resolve(second);

        Assert.That(options.ActionTimeout, Is.EqualTo(TimeSpan.FromSeconds(7)));
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
        public string Name => "Fake";
        public FakeBackend Backend { get; } = new();
        public Exception? Failure { get => Backend.Failure; init => Backend.Failure = value; }
        public ValueTask<IWebBackend> CreateAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
        {
            Backend.Context = context;
            return ValueTask.FromResult<IWebBackend>(Backend);
        }
    }

    private sealed class FakeBackend : IWebBackend
    {
        public string Name => "Fake";
        public List<(string Kind, WebElementReference? Element, string? Value)> Operations { get; } = [];
        public Exception? Failure { get; set; }
        public ProtoExecutionContext? Context { get; set; }
        public bool AddAttachmentOnComplete { get; set; }
        public int CountResult { get; set; }
        public Queue<string> TextResults { get; } = new();
        public string? ValueResult { get; set; }
        public bool Disposed { get; private set; }

        public ValueTask NavigateAsync(Uri address, CancellationToken cancellationToken = default)
        {
            Operations.Add(("navigate", null, address.ToString()));
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
            return ValueTask.FromResult(TextResults.Count == 0 ? "text" : TextResults.Dequeue());
        }

        public ValueTask<string?> ReadValueAsync(WebElementReference element, CancellationToken cancellationToken = default)
        {
            Operations.Add(("value", element, null));
            return ValueTask.FromResult(ValueResult);
        }

        public ValueTask<bool> IsVisibleAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);

        public ValueTask<bool> IsEnabledAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);

        public ValueTask<bool> IsCheckedAsync(WebElementReference element, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);

        public Queue<bool> EvaluateResults { get; } = new();
        public List<string> EvaluatedScripts { get; } = [];

        public ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
        {
            EvaluatedScripts.Add(script);
            return ValueTask.FromResult(EvaluateResults.Count == 0 || EvaluateResults.Dequeue());
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

    private sealed class StubWebDriver : OpenQA.Selenium.IWebDriver
    {
        public bool QuitCalled { get; private set; }
        public string Url { get; set; } = "https://example.test/";
        public string Title => "Stub browser";
        public string PageSource => "<html></html>";
        public string CurrentWindowHandle => "window";
        public System.Collections.ObjectModel.ReadOnlyCollection<string> WindowHandles => new([CurrentWindowHandle]);
        public void Close() { }
        public void Quit() => QuitCalled = true;
        public OpenQA.Selenium.IWebElement FindElement(OpenQA.Selenium.By by) => throw new OpenQA.Selenium.NoSuchElementException();
        public System.Collections.ObjectModel.ReadOnlyCollection<OpenQA.Selenium.IWebElement> FindElements(OpenQA.Selenium.By by) => new([]);
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
