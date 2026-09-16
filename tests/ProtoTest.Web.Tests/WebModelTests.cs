namespace ProtoTest.Web.Tests;

using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Web.Selenium;
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

        public ValueTask<bool> EvaluateBooleanAsync(string script, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);

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
