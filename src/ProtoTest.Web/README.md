# ProtoTest.Web

The web API shared by the Playwright and Selenium backends.

Install `ProtoTest.Web.Playwright` or `ProtoTest.Web.Selenium` in a test project. They bring this package in transitively.

## Why is this separate from the backends?

Page objects, components, tables and browser actions work the same way on either backend. The backend handles the browser; this package holds the test-facing API.

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
