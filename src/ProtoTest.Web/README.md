# ProtoTest.Web

The web API shared by the Playwright and Selenium backends.

Install `ProtoTest.Web.Playwright` or `ProtoTest.Web.Selenium` in a test project. They bring this package in transitively.

## Why is this separate from the backends?

I wanted page objects, components, tables and browser actions to work the same way without tying them to one browser library.

The backend handles the browser. This package contains the API used by tests, including named sessions, reusable flows, login strategies, waits and page coverage.

```csharp
var page = Proto.Context.Web().Page<ProjectsPage>();
await page.OpenAsync("/projects");
await page.Search.FillAsync("atlas");
await page.Results.RowMatching(By.HasText("atlas")).Should.BeVisibleAsync();
```

## Learn more

- [Web integration](https://prototest.dev/docs/integrations/web/)
- [Page objects](https://prototest.dev/docs/integrations/web/page-objects)
- [Web diagnostics](https://prototest.dev/docs/integrations/web/diagnostics)
