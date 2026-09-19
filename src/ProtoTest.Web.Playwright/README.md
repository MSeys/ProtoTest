# ProtoTest.Web.Playwright

The Playwright execution backend for `ProtoTest.Web`, with a per-test browser pool and an opt-in install probe.

```bash
dotnet add package ProtoTest.Web.Playwright
```

## Quick start

```csharp
builder.AddWeb(options =>
{
    options.Browser = PlaywrightBrowser.Chromium;   // the default
    options.InstallBrowsers = true;                 // download it when missing (clean machines, CI)
    options.TraceRetention = PlaywrightTraceRetention.OnWebFailure;
});

// Gate browser tests when the machine may not have the browser installed.
[RequiresPlaywrightBrowser]
[ProtoTest]
public async Task Projects_render() { }
```

## What it adds

- **Backend** — `AddWeb(options?)` on the host builder or an application registers the Playwright backend, the browser pool and the `Playwright` browser capability.
- **Native escape hatch** — `web.GetBackendAsync<PlaywrightWebBackend>()` exposes `Page` and `BrowserContext`; the synchronous `GetBackend<PlaywrightWebBackend>()` works only after initialization.
- **Skip probe** — `[RequiresPlaywrightBrowser]`, `[RequiresPlaywrightBrowser(channel: "msedge")]` and `[RequiresPlaywrightBrowser(Session = "Admin")]` probe without launching a browser; `InstallBrowsers = true` never skips.
- **Diagnostics** — console, page-error and request-failure events; on failure a full-page screenshot, DOM, location and (per retention) a native `playwright-{session}-trace.zip`.
- **Page coverage** — `CurrentAddress` is `Page.Url`, so a navigation is attributed to the page it landed on after redirects.

## Configuration

Under `ProtoTest:Web:Playwright`, and per session under `ProtoTest:Web:Sessions:{name}` (session wins).

| Key | Type | Default |
| --- | --- | --- |
| `Browser` | `PlaywrightBrowser` (`Chromium`, `Firefox`, `Webkit`) | `Chromium` |
| `Headless` | `bool` | `true` |
| `SlowMo` | `float?` (ms) | `null` |
| `Channel` | `string?` (`msedge`, `chrome`, …) | `null` |
| `InstallBrowsers` | `bool` | `false` |
| `Context` | `BrowserNewContextOptions` (nested keys bind) | `new()` |
| `TraceRetention` | `PlaywrightTraceRetention` (`Off`, `OnWebFailure`, `Always`) | `OnWebFailure` |
| `CorrelateTraceGroups` | `bool` | `true` |
| `ConsoleCapture` | `PlaywrightConsoleCapture` (`Off`, `Errors`, `WarningsAndErrors`, `All`) | `WarningsAndErrors` |
| `CapturePageErrors` | `bool` | `true` |
| `CaptureRequestFailures` | `bool` | `true` |

The browser pool is scoped to one test: sessions of a test share a process only when their launch options match. `InstallBrowsers` is ignored when `Channel` is set, and only a real launch can prove a channel works.

## Learn more

- [Web guide](https://prototest.dev/docs/integrations/web/)
- [Playwright conformance tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Web.Tests/PlaywrightConformanceTests.cs)
