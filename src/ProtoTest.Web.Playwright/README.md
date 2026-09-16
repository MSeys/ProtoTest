# ProtoTest.Web.Playwright

Playwright execution backend for `ProtoTest.Web`.

```csharp
var host = new ProtoHostBuilder()
    .AddPlaywrightWeb(options =>
    {
        options.Browser = PlaywrightBrowser.Chromium;
        options.TraceRetention = PlaywrightTraceRetention.OnWebFailure;
    })
    .Build();
```

Playwright's native locator and actionability behavior is preserved. ProtoTest adds semantic operations and best-effort screenshot, DOM, location, and native trace attachments when a Web operation fails. Use `web.GetBackend<PlaywrightWebBackend>().Page` as an explicit native escape hatch.
