# ProtoTest.Web.Playwright

The Playwright backend for `ProtoTest.Web`.

```bash
dotnet add package ProtoTest.Web.Playwright
```

Register it with `AddWeb()`. Each test receives its own browser pool and can use the shared page-object, action and assertion API from `ProtoTest.Web`.

On failure the backend can capture a screenshot, DOM, location, console information and a native Playwright trace.

## Learn more

- [Web integration](https://prototest.dev/docs/integrations/web/)
- [Web diagnostics](https://prototest.dev/docs/integrations/web/diagnostics)
- [Playwright conformance tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Web.Tests/PlaywrightConformanceTests.cs)
