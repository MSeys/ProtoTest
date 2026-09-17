# ProtoTest.Web.Playwright

Playwright execution backend for `ProtoTest.Web`.

```csharp
var host = new ProtoHostBuilder()
    .AddWeb(options =>
    {
        options.Browser = PlaywrightBrowser.Chromium;
        options.TraceRetention = PlaywrightTraceRetention.OnWebFailure;
    })
    .Build();
```

Within a test, sessions with identical launch options share one browser process while each gets its own isolated browser context; the browser is disposed with the test. Playwright's native locator and actionability behavior is preserved. ProtoTest adds semantic operations and best-effort screenshot, DOM, location, and native trace attachments when a Web operation fails. Use `web.GetBackend<PlaywrightWebBackend>().Page` as an explicit native escape hatch.
