---
sidebar_position: 1
title: Overview
---

# Web

`ProtoTest.Web` is a browser-testing model — pages, components, elements, tables, flows and login — that doesn't depend on any particular browser driver. A backend package plugs a real driver in underneath:

- `ProtoTest.Web.Playwright` — launches and manages browsers for you.
- `ProtoTest.Web.Selenium` — drives any `IWebDriver` you create.

Your page objects and tests are identical for both.

```bash
dotnet add package ProtoTest.Web.Playwright    # brings ProtoTest.Web with it
# or
dotnet add package ProtoTest.Web.Selenium
```

## A first browser test

```csharp
public sealed class LoginPage : WebPage
{
    public LoginForm Form => Component<LoginForm>(By.TestId("login"));
}

public sealed class LoginForm : WebComponent
{
    public WebElement Username => Element(By.Label("Username"));
    public WebElement Password => Element(By.Label("Password"));
    public WebElement Submit => Element(By.Role(WebRole.Button, "Sign in"));
    public WebElement Status => Element(By.Role(WebRole.Status));
}
```

```csharp
[ProtoTest]
public async Task Valid_credentials_sign_in()
{
    var login = Proto.Context.Web().Page<LoginPage>();
    await login.OpenAsync("https://portal.example.test/login");

    await login.Form.Flow("Sign in")
        .Fill(form => form.Username, "matthias")
        .Fill(form => form.Password, "correct horse")
        .Click(form => form.Submit)
        .RunAsync();

    await login.Form.Status.ShouldHaveTextAsync("Signed in");
}
```

## Registering a session

### Playwright

```csharp
builder.AddPlaywrightWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;
    options.Headless = true;
});
```

```csharp
public static IProtoHostBuilder AddPlaywrightWeb(
    this IProtoHostBuilder builder,
    Action<PlaywrightWebOptions>? configure = null,
    string name = "Default");
```

| Option | Default | |
| --- | --- | --- |
| `Browser` | `Chromium` | `Chromium`, `Firefox` or `Webkit` |
| `Headless` | `true` | |
| `SlowMo` | `null` | milliseconds between actions, for watching a run |
| `Channel` | `null` | e.g. `"msedge"` or `"chrome"` to use an installed browser |
| `Context` | `new()` | Playwright's `BrowserNewContextOptions` — viewport, locale, base URL, storage state… |
| `TraceRetention` | `OnWebFailure` | keep Playwright's own trace: `Off`, `OnWebFailure`, `Always` |
| `CorrelateTraceGroups` | `true` | group Playwright trace actions under ProtoTest operations |
| `ConsoleCapture` | `WarningsAndErrors` | `Off`, `Errors`, `WarningsAndErrors`, `All` |
| `CapturePageErrors` | `true` | uncaught page exceptions |
| `CaptureRequestFailures` | `true` | failed network requests |

Playwright needs its browsers installed once per machine — the standard `playwright.ps1 install` script from the Microsoft.Playwright package. Using `Channel = "msedge"` or `"chrome"` avoids that by driving a browser that's already installed.

Within a test, sessions registered with identical launch options (`Browser`, `Headless`, `SlowMo`, `Channel`) share one browser process, each with its own isolated browser context.

### Selenium

```csharp
builder.AddSeleniumWeb(
    () => new ChromeDriver(),
    options => options.ActionTimeout = TimeSpan.FromSeconds(10));
```

```csharp
public static IProtoHostBuilder AddSeleniumWeb(
    this IProtoHostBuilder builder,
    Func<IWebDriver> createDriver,
    Action<SeleniumWebOptions>? configure = null,
    string name = "Default");
```

| Option | Default | |
| --- | --- | --- |
| `ActionTimeout` | 5 s | how long an action retries until the element is actionable |
| `PollInterval` | 50 ms | |
| `WaitForStableBounds` | `true` | wait until the element stops moving before clicking |
| `CheckClickObstruction` | `true` | fail if something covers the element |
| `DiagnosticTraceRetention` | `OnWebFailure` | `Off`, `OnWebFailure`, `Always` |

The factory is called once per test that uses the browser; ProtoTest quits and disposes the driver afterwards.

### From configuration

Both backends also read their options from configuration, so CI can run headless on another browser without code changes. Values are applied in this order, later winning:

1. your `AddPlaywrightWeb` / `AddSeleniumWeb` callback,
2. `ProtoTest:Web:Playwright` or `ProtoTest:Web:Selenium` — every session of that backend,
3. `ProtoTest:Web:Sessions:{name}` — one named session.

```json
{
  "ProtoTest": {
    "Web": {
      "Playwright": {
        "Browser": "Firefox",
        "Headless": true,
        "TraceRetention": "Always",
        "Context": { "Locale": "nl-BE", "ViewportSize": { "Width": 1280, "Height": 720 } }
      },
      "Sessions": {
        "Admin": { "Channel": "msedge" }
      }
    }
  }
}
```

Nested Playwright context options such as `Context:Locale` bind too. Options are bound once, the first time a session of that name opens a browser, and Selenium's timeouts are validated again after binding.

## Sessions

`Proto.Context.Web(name = "Default")` returns the test's `WebSession`. The browser is created **lazily** on the first operation, so a test that never touches the browser never starts one, and it's closed during teardown.

```csharp
public sealed class WebSession : IAsyncDisposable
{
    string Name { get; }
    string BackendName { get; }
    TPage Page<TPage>() where TPage : WebPage, new();
    ValueTask<TBackend> GetBackendAsync<TBackend>(CancellationToken cancellationToken = default) where TBackend : class, IWebBackend;
    TBackend GetBackend<TBackend>() where TBackend : class, IWebBackend;
}
```

### Several browsers in one test

Register more than one session by name — for example, an administrator and a customer interacting with the same system:

```csharp
builder
    .AddPlaywrightWeb(name: "Admin")
    .AddPlaywrightWeb(name: "Customer");
```

```csharp
var admin = Proto.Context.Web("Admin").Page<BackOfficePage>();
var customer = Proto.Context.Web("Customer").Page<StorefrontPage>();
```

### Dropping down to the driver

When you need something the model doesn't offer, get the native backend:

```csharp
var backend = await Proto.Context.Web().GetBackendAsync<PlaywrightWebBackend>();
await backend.Page.SetContentAsync(html);
```

`GetBackendAsync` creates the browser if needed; the synchronous `GetBackend` throws if it hasn't been created yet. Asking for the wrong backend type throws `WebBackendCapabilityException`.

## Next

- [Pages and components](./page-objects.md)
- [Locators](./locators.md)
- [Actions and assertions](./interactions.md)
- [Flows](./flows.md)
- [Logging in](./login.md)
- [Waits and middleware](./middleware.md)
- [Diagnostics and artifacts](./diagnostics.md)
