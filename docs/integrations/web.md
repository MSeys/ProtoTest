# Web testing

`ProtoTest.Web` defines a component-oriented web-testing language independently from a browser automation library. `ProtoTest.Web.Playwright` and `ProtoTest.Web.Selenium` execute that language using their native APIs.

The initial vertical slice includes:

- pages and lazily scoped components;
- lazy component collections with zero-based and explicit one-based addressing;
- typed table and table-row helpers with numbered and header-aware cells;
- structured test-id, role, text, label, placeholder, attribute, and CSS locators;
- locator composition with `And(By.HasText(...))`;
- navigation, click, fill, check/uncheck, option selection, key press, and state reads;
- polling Web assertions for visibility, enabled/checked state, text, and input values;
- automatic semantic ProtoTrace operations with redacted fill values;
- composable operation middleware and named before/after waits;
- test-scoped backend ownership and cleanup through the Core client lifecycle;
- best-effort screenshot, DOM, and location artifacts on operation failure;
- optional native Playwright trace retention and Selenium diagnostic timeline retention, correlated with semantic ProtoTrace operations;
- explicit access to the active native backend;
- multiple named, independently isolated Web sessions (e.g. `Admin` and `Customer`) in the same test, each lazily started on first use;
- fluent multi-step `Flow()` and `InteractAsync()` helpers scoped to one component's owning session;
- application-owned login via `[LoginAs]`, without credentials ever appearing in attribute metadata or trace.

```csharp
public sealed class LoginPage : WebPage
{
    public LoginForm Form => Component<LoginForm>(By.TestId("login-form"));
}

public sealed class LoginForm : WebComponent
{
    public WebElement Email => Element(By.Label("Email"));
    public WebElement Submit => Element(By.Role(WebRole.Button, "Sign in"));
}
```

Collections and tables remain lazy too:

```csharp
public sealed class InvoiceTable : WebTable<InvoiceRow>
{
    // Useful for older tables without useful ARIA roles.
    protected override WebLocator RowLocator => By.Css("tbody > tr");
}

public sealed class InvoiceRow : WebTableRow
{
    public WebElement Number => CellNumber(1, nameof(Number));
    public WebElement Total => Cell("Total", nameof(Total));
}

var secondRow = page.Invoices.RowNumber(2); // explicitly one-based
var matching = page.Invoices.RowMatching(By.HasText("INV-123"), "Invoice[INV-123]");
var count = await page.Invoices.Rows.CountAsync();
```

Cross-cutting policies wrap semantic operations:

```csharp
builder
    .AddWebMiddleware<MyDiagnosticsMiddleware>()
    .AddWebWait<JQueryIdleWait>(
        WebWaitTiming.After,
        timeout: TimeSpan.FromSeconds(5),
        operations: [WebOperationKind.Click]);
```

Custom `IWebWaitCondition` implementations return a named observation on every poll. Timeout failures include the last observation and the wait is represented as a child operation in ProtoTrace. Without an explicit operation list, a wait applies to navigation, click, and fill—not to passive reads.

Configure one backend on the ProtoTest host:

```csharp
builder.AddPlaywrightWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;
    options.TraceRetention = PlaywrightTraceRetention.OnWebFailure;
});
```

or provide an application-owned Selenium driver factory:

```csharp
builder.AddSeleniumWeb(
    () => new ChromeDriver(),
    options => options.ActionTimeout = TimeSpan.FromSeconds(5));
```

Use the session from the active execution context:

```csharp
var login = Proto.Context.Web().Page<LoginPage>();
await login.OpenAsync("https://example.test/login");
await login.Form.Email.FillAsync("customer@example.com");
await login.Form.RememberMe.CheckAsync();
await login.Form.Language.SelectOptionAsync("nl");
await login.Form.Submit.ClickAsync();
await login.Status.ShouldHaveTextAsync("Signed in", TimeSpan.FromSeconds(5));
```

Constructing these objects performs no browser query. The backend resolves every component root and element locator when an operation runs. Playwright therefore retains its native locator and actionability behavior. Selenium deliberately avoids cached `IWebElement` instances and retries the whole interaction after missing, stale, intercepted, or temporarily non-interactable states. Its default actionability loop checks visible, enabled, readonly, stable bounds, and click-point obstruction where the driver supports JavaScript.

Selenium does not have a native equivalent of Playwright's time-travel trace. ProtoTest therefore records a backend diagnostic timeline—resolution attempts, waits, stale-element retries, actionability observations, and outcomes—and retains `selenium-<session>-diagnostics.json` according to `DiagnosticTraceRetention`. It is registered as a normal Core attachment before publishing, so it travels inside the same `.prototrace` alongside the semantic operations, screenshots, page source, URL, and title. It is not a parallel trace format or reporting pipeline.

```csharp
builder.AddSeleniumWeb(
    () => new ChromeDriver(),
    options => options.DiagnosticTraceRetention = SeleniumDiagnosticTraceRetention.OnWebFailure);
```

CSS is an explicit escape hatch. Prefer semantic locators because they produce richer traces and map to the strongest backend-native mechanism. Adapter-specific APIs remain available explicitly through `web.GetBackend<PlaywrightWebBackend>()` (throws until the session has actually started; use `await web.GetBackendAsync<PlaywrightWebBackend>()` beforehand if no operation has run yet) or `web.GetBackend<SeleniumWebBackend>()`. A browser-backed Playwright conformance test exercises the shared form, assertion, table-header, and trace behavior against local Edge; it is skipped on machines without Edge.

## Multiple sessions in one test

Register more than one named backend to drive several isolated browser sessions—each with its own cookies, storage, and trace/diagnostics—from the same test. Playwright shares one browser process per matching launch configuration and gives every named session its own `BrowserContext`; Selenium starts one driver per session:

```csharp
builder
    .AddPlaywrightWeb(options => options.Browser = PlaywrightBrowser.Chromium, name: "Admin")
    .AddPlaywrightWeb(options => options.Browser = PlaywrightBrowser.Chromium, name: "Customer");
```

```csharp
var admin = Proto.Context.Web("Admin").Page<AdminPage>();
var customer = Proto.Context.Web("Customer").Page<CustomerPage>();

await admin.OpenAsync(applicationUrl);
await customer.OpenAsync(applicationUrl);
```

A session's backend starts lazily the first time a test actually uses it (navigates, reads, or acts), not merely because it was registered. A test that registers `Admin` and `Customer` but only drives `Customer` never pays for a second browser context. Every Web operation, failure artifact, and trace attachment carries its session name (for example `playwright-admin-trace.zip`, `selenium-customer-diagnostics.json`), so multi-session traces stay unambiguous.

## Fluent multi-step interactions

`Flow()` chains several actions on one component into a single named parent operation without repeating the component path, while each step still runs through the normal middleware, waits, and redaction pipeline. Execution stops at the first failure:

```csharp
await page.LoginForm
    .Flow("Sign in")
    .Fill(x => x.Email, credentials.Email)
    .Fill(x => x.Password, credentials.Password)
    .Check(x => x.RememberMe)
    .Click(x => x.Submit)
    .RunAsync();
```

`InteractAsync` is the lighter, imperative escape hatch for conditional logic that still deserves one named parent operation in the trace:

```csharp
await page.LoginForm.InteractAsync("Sign in", async form =>
{
    await form.Email.FillAsync(credentials.Email);
    await form.Submit.ClickAsync();
});
```

A flow always runs against the session that owns the component it started from, so `admin.LoginForm.Flow(...)` and `customer.LoginForm.Flow(...)` never cross sessions.

## Logging in via an attribute

`[LoginAs]` runs an application-owned `IWebLoginStrategy` during test setup, traced as a `Setup` child operation named after the persona and session. The attribute only ever carries a persona/role and a session name—never credentials—so secrets stay out of test metadata and ProtoTrace:

```csharp
public sealed class ExampleLogin : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var page = context.Web.Page<LoginPage>();
        await page.OpenAsync("https://example.test/login");
        await page.Form.Email.FillAsync(LookUpEmail(context.Persona));
        await page.Form.Password.FillAsync(LookUpPassword(context.Persona));
        await page.Form.Submit.ClickAsync();
    }
}

// builder.ConfigureServices(services => services.AddSingleton<IWebLoginStrategy, ExampleLogin>());

[LoginAs("Administrator", session: "Admin")]
[LoginAs("Customer", session: "Customer")]
[Test]
public async Task AdminCanObserveCustomerOrder() { /* ... */ }
```

`WebLoginContext.Mode` (`UserInterface`, `Api`, `StorageState`) lets the strategy pick a faster path than driving the real login form when representativeness is not the point of the test; ProtoTest does not interpret the mode itself.

## Playwright native trace correlation

When `TraceRetention` is not `Off` and `CorrelateTraceGroups` is enabled (both true by default), the Playwright adapter wraps every semantic Web operation in a native `Tracing.GroupAsync`/`GroupEndAsync` pair named with the same correlation id and session as its ProtoTrace operation. Opening the retained `playwright-<session>-trace.zip` in Playwright's own trace viewer therefore lines up 1:1 with the ProtoTrace timeline instead of showing an undifferentiated action log.

`ConsoleCapture`, `CapturePageErrors`, and `CaptureRequestFailures` forward browser console messages, uncaught page errors, and failed network requests into ProtoTrace as child events of whichever semantic operation was active when they fired (or as top-level events outside of one). These stay lightweight signals in ProtoTrace itself—full request/response bodies and DOM snapshots remain in the native Playwright trace, not duplicated into ProtoTrace.
