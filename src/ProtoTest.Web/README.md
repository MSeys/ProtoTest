# ProtoTest.Web

The backend-neutral web-testing model: semantic locators, page objects, component scoping, flows, named waits, login and page coverage.

```bash
dotnet add package ProtoTest.Web
```

Register a backend — `ProtoTest.Web.Playwright` or `ProtoTest.Web.Selenium` — which supplies `AddWeb`; this package brings the model that both drive.

## Quick start

```csharp
public sealed class ProjectsPage : WebPage
{
    public WebElement Search => Element(By.TestId("search"));
    public ProjectTable Projects => Component<ProjectTable>(By.TestId("projects"));
}

public sealed class ProjectTable : WebTable<ProjectRow>;
public sealed class ProjectRow : WebTableRow;

var page = Proto.Context.Web().Page<ProjectsPage>();
await page.OpenAsync("/projects");
await page.Search.FillAsync("atlas");

var row = page.Projects.RowMatching(By.HasText("atlas"));
await row.Cell("Project").Should.HaveTextAsync("atlas");
```

## What it adds

- **Sessions** — `Proto.Context.Web(name?, application?)` creates a per-test session lazily; `[WebSession(name, Open = …, Application = …)]` and `[LoginAs<TStrategy>(persona, Session = …)]` cover setup and login.
- **Model** — `WebPage`/`WebComponent` with `Element`, `Component<T>()` and `Components<T>()`; `WebTable<TRow>` and `WebTableRow` for tables; every operation carries its scope path.
- **Actions and assertions** — `ClickAsync`, `FillAsync`, `CheckAsync`, `SelectOptionAsync`, `PressAsync`, reads, and `element.Should`/`ShouldNot` (`BeVisibleAsync`, `BeEnabledAsync`, `HaveTextAsync`, `ContainTextAsync`, `HaveValueAsync`, …).
- **Flows, middleware and waits** — `Flow<T>()`/`InteractAsync`, `AddWebMiddleware<T>()`, `AddWebWait<TCondition>(timing, …)` and `WaitUntilAsync`.
- **Coverage** — a successful navigation records `web.page.visited`, a passing assertion `web.page.verified`, and inventory records `web.page.available`; `WebCoverageCollector` reports one item per page.
- **Tracing** — `web.navigate`, `web.click`, `web.fill` (redacted value), `web.press`, `assert.web` and friends, with `web.backend.execute` child entries.

## Configuration

Session sections under `ProtoTest:Web:Sessions:{name}`; backend options also bind over the same section.

| Key | Type | Default |
| --- | --- | --- |
| `Application` | `string` | session name |
| `BaseUrl` | absolute URI string | `ProtoTest:Applications:{application}:BaseUrl` |
| `Open` | `string` (absolute or relative) | `[WebSession].Open` |
| `DiscoverRoutes` | `bool` | `false` |

Coverage inventory lives under `ProtoTest:Web:Pages` with `ProtoTest:Web:Pages:Source` (`auto`/`next`/`nuxt`/`remix`/`vue`/`react`) and `Framework`; in-process ASP.NET Core pages come from their own inventory.

One backend per host: resolving zero or more than one `IWebBackendFactory` throws, and `AddWebBackend` keeps the first registration. `HasText` must be composed with `And` on both backends.

## Learn more

- [Web guide](https://prototest.dev/docs/integrations/web/)
- [WebJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/WebJourney.cs)
