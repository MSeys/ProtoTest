# ProtoTest.Web.Selenium

Selenium execution backend for `ProtoTest.Web`.

```csharp
var host = new ProtoHostBuilder()
    .AddSeleniumWeb(
        () => new ChromeDriver(),
        options => options.ActionTimeout = TimeSpan.FromSeconds(8))
    .Build();
```

Each interactive operation uses a bounded actionability loop. Selenium re-resolves component scopes and the target after missing or stale elements, waits for visible and enabled state, checks readonly state for editing, waits for stable bounds before click/check, checks click-point obstruction when JavaScript is available, and retries intercepted or temporarily non-interactable actions. These are backend actionability checks; application waits remain named `ProtoTest.Web` conditions.

The adapter resolves lazy references afresh for each operation, retries stale references, and waits for the named element to exist, be visible, and be enabled. Failure capture adds the screenshot (when supported), page source, URL, and title through Core attachments.

`DiagnosticTraceRetention` defaults to `OnWebFailure`. The retained `selenium-diagnostics.json` records the actionability timeline and is a regular Core attachment inside the `.prototrace`; it is deliberately not a second trace/reporting system. Use `Always` while investigating flaky tests or `Off` when no backend diagnostics should be retained.

Use `web.GetBackend<SeleniumWebBackend>().Driver` as an explicit native escape hatch.
