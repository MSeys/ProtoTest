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

        public ValueTask<string?> GetCurrentAddressAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(CurrentAddress);

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

    private sealed class FakeElement(string text) : OpenQA.Selenium.IWebElement
    {
        public string TagName => "div";
        public string Text { get; } = text;
        public bool Enabled => true;
        public bool Selected => false;
        public System.Drawing.Point Location => default;
        public System.Drawing.Size Size => default;
        public bool Displayed => true;
        public void Clear() { }
        public void SendKeys(string value) { }
        public void Submit() { }
        public void Click() { }
        public string GetAttribute(string attributeName) => string.Empty;
        public string GetCssValue(string propertyName) => string.Empty;
        public string? GetDomAttribute(string attributeName) => null;
        public string? GetDomProperty(string propertyName) => null;
        public string? GetProperty(string propertyName) => null;
        public OpenQA.Selenium.ISearchContext GetShadowRoot() => throw new NotSupportedException();
        public OpenQA.Selenium.IWebElement FindElement(OpenQA.Selenium.By by)
            => throw new OpenQA.Selenium.NoSuchElementException();
        public System.Collections.ObjectModel.ReadOnlyCollection<OpenQA.Selenium.IWebElement> FindElements(
            OpenQA.Selenium.By by) => new([]);
        public void Dispose() { }
    }

    private sealed class StubWebDriver : OpenQA.Selenium.IWebDriver, OpenQA.Selenium.ITakesScreenshot
    {
        private string _url = "https://example.test/";

        public bool QuitCalled { get; private set; }
        public bool ThrowOnUrl { get; set; }
        public List<OpenQA.Selenium.By> FindAllQueries { get; } = [];

        /// <summary>The threads that ran a driver call, so a test can prove they all share the pump.</summary>
        public HashSet<int> CallerThreads { get; } = [];

        /// <summary>Supplies the elements a lookup returns; empty when unset.</summary>
        public Func<OpenQA.Selenium.By, IReadOnlyList<OpenQA.Selenium.IWebElement>>? Elements { get; set; }
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
            CallerThreads.Add(Environment.CurrentManagedThreadId);
            FindAllQueries.Add(by);
            return new((Elements?.Invoke(by) ?? []).ToList());
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
