---
sidebar_position: 2
title: Pages and components
description: "Model a browser UI with pages, components, elements and tables, so tests read as what a user does rather than as markup."
---

# Pages and components

The web model has four building blocks:

| Type | What it is |
| --- | --- |
| `WebPage : WebComponent` | a top-level page you can open |
| `WebComponent` | a reusable part of a page — a form, a dialog, a navigation bar |
| `WebElement` | one thing you act on or assert against |
| `WebTable<TRow> : WebComponent` / `WebTableRow : WebComponent` | a table and its rows |

You describe them as classes with **properties**, and ProtoTest builds a fresh element every time a property is read. Nothing is cached — each action resolves its element again against the live page, which is why the model tolerates re-rendering front-ends.

## Pages

```csharp
public sealed class InvoicesPage : WebPage
{
    public WebElement Heading => Element(By.Role(WebRole.Heading, "Invoices"));
    public InvoiceTable Table => Component<InvoiceTable>(By.TestId("invoice-table"));
}
```

```csharp
var page = Proto.Context.Web().Page<InvoicesPage>();
await page.OpenAsync("https://portal.example.test/invoices");
```

`Page<T>()` doesn't navigate — it gives you a page object bound to the session, one instance per page type per session. `OpenAsync` navigates:

```csharp
ValueTask OpenAsync(string address, CancellationToken cancellationToken = default);
ValueTask OpenAsync(Uri address, CancellationToken cancellationToken = default);
```

A relative address is resolved against the session's base URL before either backend sees it — `ProtoTest:Web:Sessions:{name}:BaseUrl`, or the targeted application's `ProtoTest:Applications:{application}:BaseUrl` (see [Sessions](./index.md#sessions)). With no base URL configured, a relative address throws an `InvalidOperationException` naming both keys. An absolute address is used as given.

## Components

`WebComponent` is the base for everything, including `WebPage`. It gives you three protected factory methods:

```csharp
protected WebElement Element(WebLocator locator, [CallerMemberName] string? name = null);

protected TComponent Component<TComponent>(WebLocator? root = null, [CallerMemberName] string? name = null)
    where TComponent : WebComponent, new();

protected WebComponentCollection<TComponent> Components<TComponent>(WebLocator items, [CallerMemberName] string? name = null)
    where TComponent : WebComponent, new();
```

The `name` defaults to the property name (or the component type name), and shows up in traces and failure messages as a path like `InvoicesPage.Table.Invoice.Open`. That's why properties read better than local variables.

### Scoping

A component with a `root` locator scopes everything inside it: `Element(...)` calls within the component only search inside the root.

```csharp
public sealed class CheckoutPage : WebPage
{
    public AddressForm Billing => Component<AddressForm>(By.TestId("billing"));
    public AddressForm Shipping => Component<AddressForm>(By.TestId("shipping"));
}

public sealed class AddressForm : WebComponent
{
    public WebElement Street => Element(By.Label("Street"));
}
```

`page.Billing.Street` and `page.Shipping.Street` find different fields even though both are labelled "Street" — one reusable class, two scopes. Each `root` appends one level to the component path (`{parentPath}.{name}`).

`Component<T>()` without a root doesn't add a scope level; it just groups elements under a name.

### The session inside a component

`WebComponent` also exposes `protected WebSession Web { get; }`, for components that need to open another page or reach the backend.

Components must be created through `Page<T>()`, `Component<T>()` or `Components<T>()` — instantiating one with `new` and using it throws, and a component can only be initialised once.

## Lists of components

```csharp
public sealed class InboxPage : WebPage
{
    public WebComponentCollection<MessageCard> Messages =>
        Components<MessageCard>(By.Role(WebRole.ListItem));
}
```

```csharp
public sealed class WebComponentCollection<TComponent>
{
    ValueTask<int> CountAsync(CancellationToken cancellationToken = default);
    TComponent At(int index, string? name = null);          // zero-based
    TComponent Number(int number, string? name = null);     // one-based
    TComponent First(string? name = null);
    TComponent Matching(WebLocator condition, string? name = null);
}
```

`Matching` composes its condition with the collection's locator using `And`, so it is strict: no match or more than one match is an error, not a default. A generated component name includes the index, e.g. `Messages[1]`.

```csharp
var count = await inbox.Messages.CountAsync();
await inbox.Messages.Number(1).Subject.Should.HaveTextAsync("Welcome");
await inbox.Messages.Matching(By.HasText("Invoice INV-123")).Open.ClickAsync();
```

## Tables

`WebTable<TRow>` finds rows by `role=row` by default. Override `RowLocator` if your markup differs.

```csharp
public sealed class InvoiceTable : WebTable<InvoiceRow>
{
    public InvoiceRow Invoice(string number) =>
        Component<InvoiceRow>(By.Role(WebRole.Row).And(By.HasText(number)), nameof(Invoice));
}

public sealed class InvoiceRow : WebTableRow
{
    public WebElement Open => Element(By.Role(WebRole.Link, "Open"));
    public WebElement Number => Cell("Invoice number", name: nameof(Number));
    public WebElement Total => Cell("Total", name: nameof(Total));
}
```

```csharp
public abstract class WebTable<TRow> : WebComponent where TRow : WebComponent, new()
{
    protected virtual WebLocator RowLocator => By.Role(WebRole.Row);
    WebComponentCollection<TRow> Rows { get; }
    TRow RowAt(int index, string? name = null);          // zero-based
    TRow RowNumber(int number, string? name = null);     // one-based
    TRow RowMatching(WebLocator condition, string? name = null);
}

public abstract class WebTableRow : WebComponent
{
    WebElement CellAt(int index, string? name = null);                  // zero-based
    WebElement CellNumber(int number, string? name = null);             // one-based
    WebElement Cell(string header, string? name = null, bool exact = true, bool ignoreCase = false);
}
```

Rows are components, so a row class can hold nested components and elements exactly like any other component, and it can itself be used as the `TRow` of a `WebTable`. `Cell("Total")` finds the cell in the column whose conventional header cell reads "Total", so tests keep working when columns are reordered; the header lookup uses `ancestor::table[1]//tr[1]`.

```csharp
await page.Table.Invoice("INV-123").Total.Should.HaveTextAsync("€ 10");
await page.Table.RowNumber(2).Cell("Total").Should.HaveTextAsync("€ 10");
```

Note that row numbers count every `role=row`, **including the header row** — `RowNumber(2)` is the first data row in a table with one header row.

## Element references

Every `WebElement` carries a `WebElementReference` describing where it lives — [`WaitUntilAsync`](./index.md#sessions) predicates and [custom waits](./middleware.md#wait-conditions) consume it:

```csharp
string Name { get; }                    // e.g. "Submit"
string ComponentPath { get; }           // e.g. "LoginPage.Form.Submit"
WebLocator Locator { get; }
WebElementReference Reference { get; }  // scope roots, path, name and locator
```

## Next

- [Locators](./locators.md) — how the `By` factory and each backend find these elements.
- [Actions and assertions](./interactions.md) — what you can do with an element.
