# ProtoTest.Web.Playwright

Playwright execution backend for `ProtoTest.Web`.

```csharp
var host = new ProtoHostBuilder()
    .AddWeb(options =>
    {
        options.Browser = PlaywrightBrowser.Chromium;   // the default, runs on Windows, Linux and macOS
        options.InstallBrowsers = true;                 // download it when missing (clean machines, CI)
        options.TraceRetention = PlaywrightTraceRetention.OnWebFailure;
    })
    .Build();
```

`InstallBrowsers` downloads the selected browser through the Playwright driver before the first launch, so a clean machine or CI runner needs no separate `playwright install` step; it is ignored when `Channel` names a system browser (`msedge`, `chrome`). On a clean Linux image the operating-system libraries still come from `playwright.ps1 install --with-deps chromium`.

When even an installed browser may be absent, gate browser tests with the opt-in skip condition instead of hand-rolling `Assert.Ignore`:

```csharp
[RequiresPlaywrightBrowser]                      // the browser from configuration
[RequiresPlaywrightBrowser(channel: "msedge")]
[RequiresPlaywrightBrowser(Session = "Admin")]   // the Admin session's options
public async Task ...() { ... }
```

It probes the configured browser before setup without launching one — through the driver's `BrowserType.ExecutablePath` for bundled browsers, and through Playwright's own dry-run channel check for channels — and skips with a reason naming the missing browser and the install options. `InstallBrowsers = true` (in code or configuration) means never skip, because the browser is downloaded on demand. Set `Session` when the test drives a named session: the probe then merges `ProtoTest:Web:Sessions:{name}` over `ProtoTest:Web:Playwright`, exactly like the backend binds. This is the stronger gate than `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Playwright")]`, which only proves `AddWeb` registered the backend.

Within a test, sessions with identical launch options share one browser process while each gets its own isolated browser context; the browser is disposed with the test. Playwright's native locator and actionability behavior is preserved. ProtoTest adds semantic operations and best-effort screenshot, DOM, location, and native trace attachments when a Web operation fails. Use `web.GetBackend<PlaywrightWebBackend>().Page` as an explicit native escape hatch.

The backend reports the page's current address (`Page.Url`), so page coverage attributes a navigation to the page it actually landed on after redirects. See [Page coverage](../../docs/docs/integrations/web/index.md#page-coverage).
