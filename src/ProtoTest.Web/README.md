# ProtoTest.Web

`ProtoTest.Web` is the backend-neutral web-testing model for ProtoTest. It provides semantic locators, lazy element references and collections, typed table helpers, component scoping, operation middleware, named waits, and automatic ProtoTrace operations.

Browser resources and native behavior live in adapter packages such as `ProtoTest.Web.Playwright` and `ProtoTest.Web.Selenium`.

```csharp
public sealed class LoginPage : WebPage
{
    public LoginForm Login => Component<LoginForm>();
}

public sealed class LoginForm : WebComponent
{
    public WebElement Username => Element(By.TestId("username"));
    public WebElement Submit => Element(By.Role(WebRole.Button, "Sign in"));
}

var login = Proto.Context.Web().Page<LoginPage>();
await login.OpenAsync("https://example.test/login");
await login.Login.Username.FillAsync("matthias");
await login.Login.Submit.ClickAsync();
```

`WebElement` stores a semantic scope and locator. It never caches a Playwright locator, Selenium element, or DOM node. Each operation is resolved by the active backend and automatically records intent, component path, locator, backend, outcome, and failure diagnostics in ProtoTrace. Fill values are redacted.

Collections support both idiomatic zero-based addressing (`Rows.At(0)`) and explicit one-based numbering (`Rows.Number(1)`) for older applications. Derive tables from `WebTable<TRow>` and rows from `WebTableRow`; override the row locator when a legacy table needs CSS such as `tbody > tr`.

Register cross-cutting behavior through `AddWebMiddleware<T>()`. Named application waits can be placed before or after selected semantic operations with `AddWebWait<TCondition>()`; backend actionability remains the responsibility of the Playwright or Selenium adapter.

Sessions are per-test: call `Proto.Context.Web("Admin")` / `Proto.Context.Web("Customer")` where the test needs them, and each isolated session is created lazily on first use (configure a name through `ProtoTest:Web:Sessions:{name}`). Chain several actions on one component with `Flow()`, or log a named session in during setup with `[LoginAs<AdminUiLogin>("Administrator", Session = "Admin")]` against an application-owned `IWebLoginStrategy`. See [docs/integrations/web/index.md](../../docs/docs/integrations/web/index.md) for details.

Page coverage is built in: a successful navigation records `web.page.visited`, a passing `Should*` assertion records `web.page.verified`, and the `WebCoverageCollector` registered by `AddWeb` reports one item per page — covered only when something on it was actually verified. Pages a suite knows about are listed under `ProtoTest:Web:Pages`; point `ProtoTest:Web:Pages:Source` at a frontend folder and ProtoTest also inventories Next.js/Nuxt/Remix file routes and Vue/React Router literals (`Framework` overrides the `auto` detection), with `/users/42` covering a `/users/{id}` inventory pattern. In-process ASP.NET Core applications and (opt-in) Vue routers add their own inventory as `web.page.available`; the ASP.NET Core inventory is run-level and recorded once, by the first test that initializes the server (a failed inventory is retried), and pages observed on another origin than the session's `BaseUrl` never count as this application's coverage. See [Page coverage](../../docs/docs/integrations/web/index.md#page-coverage).

When the machine may not have a browser installed, `ProtoTest.Web.Playwright` ships `[RequiresPlaywrightBrowser]` — an opt-in [skip condition](../../docs/docs/foundation/skip-conditions.md#requiring-a-playwright-browser) that probes without launching one and skips with a reason; it is the stronger gate than `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Playwright")]`, which only proves `AddWeb` was called. Selenium, whose driver is caller-created, has no equivalent probe: combine `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` with the documented try/catch.
