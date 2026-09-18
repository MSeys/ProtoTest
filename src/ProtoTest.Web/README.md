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

Sessions are per-test: call `Proto.Context.Web("Admin")` / `Proto.Context.Web("Customer")` where the test needs them, and each isolated session is created lazily on first use (configure a name through `ProtoTest:Web:Sessions:{name}`). Chain several actions on one component with `Flow()`, or log a named session in during setup with `[LoginAs<AdminUiLogin>("Administrator", Session = "Admin")]` against an application-owned `IWebLoginStrategy`. See [docs/integrations/web.md](../../docs/integrations/web.md) for details.
