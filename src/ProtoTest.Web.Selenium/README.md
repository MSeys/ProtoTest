# ProtoTest.Web.Selenium

The Selenium execution backend for `ProtoTest.Web`, with its own bounded actionability loop.

```bash
dotnet add package ProtoTest.Web.Selenium
```

## Quick start

```csharp
builder.AddWeb(
    () => new ChromeDriver(),
    options => options.ActionTimeout = TimeSpan.FromSeconds(8));

// The driver comes from your factory, so skip the test yourself when it may be missing.
[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]
[ProtoTest]
public async Task Projects_render() { }
```

## What it adds

- **Backend** — `AddWeb(createDriver, options?)` on the host builder or an application registers the Selenium backend, the `Selenium` browser capability and the `"Web"`/`"Default"` client; `createDriver` runs once per session that creates a backend.
- **Native escape hatch** — `web.GetBackendAsync<SeleniumWebBackend>()` exposes the live `IWebDriver`.
- **Actionability** — each interactive operation re-resolves scope and target, waits for visible, enabled and (for editing) writable state, scrolls clicks into view, waits for stable bounds and checks click obstruction when JavaScript is available, retrying stale or intercepted elements.
- **Locator translation** — semantic roles resolve the implicit HTML element where the platform defines one (`tr` for `Row`, `table` for `Table`, `li` for `ListItem`, `select` for `Combobox`, …).
- **Diagnostics** — on failure a screenshot (when supported), page source, URL and title; `DiagnosticTraceRetention` controls the `selenium-{session}-diagnostics.json` attachment with the actionability timeline.

## Configuration

Under `ProtoTest:Web:Selenium`, and per session under `ProtoTest:Web:Sessions:{name}` (session wins).

| Key | Type | Default |
| --- | --- | --- |
| `ActionTimeout` | `TimeSpan` | `00:00:05` |
| `PollInterval` | `TimeSpan` | `00:00:00.050` |
| `WaitForStableBounds` | `bool` | `true` |
| `CheckClickObstruction` | `bool` | `true` |
| `DiagnosticTraceRetention` | `SeleniumDiagnosticTraceRetention` (`Off`, `OnWebFailure`, `Always`) | `OnWebFailure` |

One driver per session with no pooling, so sessions do not share cookies or storage. There is no framework browser probe — `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` proves only composition.

## Learn more

- [Web guide](https://prototest.dev/docs/integrations/web/)
- [Selenium conformance tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Web.Tests/SeleniumConformanceTests.cs)
