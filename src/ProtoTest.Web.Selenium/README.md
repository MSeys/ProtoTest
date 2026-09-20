# ProtoTest.Web.Selenium

The Selenium backend for `ProtoTest.Web`.

```bash
dotnet add package ProtoTest.Web.Selenium
```

Register it with `AddWeb(() => new ChromeDriver())` or another driver factory. The shared page-object, action and assertion API comes from `ProtoTest.Web`.

The backend waits for elements to become actionable and can capture screenshots, page source and an actionability timeline on failure. Each web session owns its own driver.

## Learn more

- [Web integration](https://prototest.dev/docs/integrations/web/)
- [Web diagnostics](https://prototest.dev/docs/integrations/web/diagnostics)
- [Selenium conformance tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Web.Tests/SeleniumConformanceTests.cs)
