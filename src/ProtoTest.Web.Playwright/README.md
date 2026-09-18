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

Within a test, sessions with identical launch options share one browser process while each gets its own isolated browser context; the browser is disposed with the test. Playwright's native locator and actionability behavior is preserved. ProtoTest adds semantic operations and best-effort screenshot, DOM, location, and native trace attachments when a Web operation fails. Use `web.GetBackend<PlaywrightWebBackend>().Page` as an explicit native escape hatch.
