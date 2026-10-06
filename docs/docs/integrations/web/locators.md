---
sidebar_position: 4
title: Locators
description: "Find elements by role, label and text rather than CSS; each backend translates a WebLocator into its native query."
---

# Locators

`By` builds a `WebLocator`: a description of how to find something, which each backend translates into its own query. Prefer locators that describe what a user sees (roles, labels, text) over ones that describe markup (CSS). They survive redesigns, and they double as accessibility checks.

:::caution[Two `By` types in a Selenium suite]
A Selenium suite imports `OpenQA.Selenium` for its driver types, and `OpenQA.Selenium.By` collides with `ProtoTest.Web.By` in any file that uses both (CS0104). Alias one of them in the page-object file, for example `using By = ProtoTest.Web.By;`, or keep the driver factory in its own file. Playwright has no second `By`.
:::

## The full list

```csharp
By.Role(WebRole role, string? name = null, bool exact = true)
By.Label(string value, bool exact = true)
By.Placeholder(string value, bool exact = true)
By.Text(string value, bool exact = false, bool ignoreCase = false)
By.TestId(string value)                     // data-testid
By.Attribute(string name, string value)
By.Css(string selector)
By.HasText(string value, bool exact = false, bool ignoreCase = false)   // filter; see below
By.At(WebLocator source, int index)         // zero-based
By.TableCell(string header, bool exact = true, bool ignoreCase = false)
By.TableCellNumber(int number)              // one-based
By.TableCellAt(int index)                   // zero-based
```

In order of preference:

| Locator | Use it for | Selenium limit |
| --- | --- | --- |
| `Role` | buttons, links, headings, checkboxes, rows: anything with an ARIA role and an accessible name | implicit HTML mappings instead of ARIA resolution |
| `Label` | form fields with a `<label>` | none |
| `Placeholder` | fields without a label (consider adding one) | none |
| `Text` | static text | XPath 1.0 deepest-match |
| `TestId` | elements with no good semantic handle; `data-testid="…"` | none |
| `Attribute` / `Css` | last resort | `Css` cannot be the left side of `And` |

The full per-backend translation lives in [How each backend translates a locator](#how-each-backend-translates-a-locator).

`Attribute` accepts only names made of letters, digits, `-`, `_` and `:`. Anything else throws an `ArgumentException` when the locator is built.

## Roles

```csharp
Element(By.Role(WebRole.Button, "Save"))
Element(By.Role(WebRole.Heading, "Invoices"))
Element(By.Role(WebRole.Status))            // any element with role=status
```

`WebRole` covers: `Alert`, `Button`, `Checkbox`, `Combobox`, `Dialog`, `Grid`, `Heading`, `Image`, `Link`, `List`, `ListItem`, `Menu`, `MenuItem`, `Navigation`, `Option`, `ProgressBar`, `Radio`, `Region`, `Row`, `RowGroup`, `Searchbox`, `Slider`, `SpinButton`, `Status`, `Switch`, `Tab`, `Table`, `TabList`, `TabPanel`, `Textbox`, `Toolbar`, `Tooltip`, `Tree`, `TreeItem`.

## Combining with `And`

`And` narrows a locator. Its main use is filtering by text:

```csharp
Component<InvoiceRow>(By.Role(WebRole.Row).And(By.HasText("INV-123")))
```

`HasText` is **only valid as the right-hand side of `And`**. On its own, both backends throw `WebBackendCapabilityException`.

:::caution[Selenium limitations]
Selenium supports a narrower set of combinations: the right-hand side of `And` must be `HasText`, and the left-hand side cannot be `By.Css`. Playwright is not limited to those two forms.
:::

In Playwright, `And(By.HasText(...))` becomes a filter on the left locator, and any other right-hand side becomes Playwright's own `And`. Selenium throws `WebBackendCapabilityException` for the combinations it does not support.

## Picking the nth match

```csharp
Element(By.At(By.Role(WebRole.Button, "Remove"), 2))   // the third "Remove" button
```

The index is zero-based and must not be negative. Playwright applies `.Nth(index)`. Selenium finds all matches and picks from that list.

An index past the last match counts as a missing element. An action or assertion waits until its timeout, then fails with `WebElementResolutionException` naming the locator. A direct resolution reports the count instead (`NoSuchElementException` on Selenium).

For repeated components, [`Components<T>()`](./page-objects.md#lists-of-components) with `At`, `Number`, `First` and `Matching` usually reads better.

## Table cells

Inside a `WebTableRow`, use the row's `Cell(...)`, `CellNumber(...)` and `CellAt(...)` methods rather than these locators. They are built on `By.TableCell*` and give the element a readable name.

## Describing a locator

Every locator can describe itself with `Describe()`, and that description is what appears in traces (`web.locator`, `web.component.roots`) and failure messages:

```csharp
By.Role(WebRole.Row).And(By.HasText("INV-123", exact: true)).Describe()
// Role(Row).And(HasText("INV-123", exact: true, ignoreCase: false))
```

## How each backend translates a locator

Both backends resolve the component scope first, from the outermost root inward, then the element locator. Each translates into its own query language (`src/ProtoTest.Web.Playwright/PlaywrightWebBackend.cs`, `src/ProtoTest.Web.Selenium/SeleniumLocatorTranslator.cs`):

| Locator | Playwright | Selenium | Selenium limits |
| --- | --- | --- | --- |
| `TestId` | `GetByTestId` | XPath `.//*[@data-testid='…']` | none |
| `Role` | `GetByRole` with `AriaRole`, name and `Exact` | XPath role predicate with implicit HTML mappings (see below) | no separate `Name`/`Exact` handling beyond the predicate |
| `Text` | a plain string only when exact and case-sensitive; otherwise a regex (`^…$` when exact) with `IgnoreCase` | deepest-match XPath on `normalize-space(.)`, optional `translate(...)` lowercasing | XPath 1.0 only |
| `Label` | `GetByLabel` with `Exact` | union of `@aria-label`, a `label` wrapping an input/textarea/select, or a field matched by `label[@for]` | none |
| `Placeholder` | `GetByPlaceholder` with `Exact` | exact `@placeholder='…'`, or `contains(@placeholder, '…')` when not exact | none |
| `Css` | `Page.Locator` / scope `Locator` | `By.CssSelector` | cannot be the left side of `And` |
| `Attribute` | CSS attribute selector with escaping | XPath; a namespaced name is matched by `@*[name()='ns:name']` | name validity enforced by `By.Attribute` |
| `At(n)` | `.Nth(n)` | all matches resolved, then indexed in memory; out of range throws `NoSuchElementException` | none |
| `TableCellAt` | `th, td` nth from the page; `:scope > th, :scope > td` inside a component | document-scoped from the driver, element-scoped from a component | the axis depends on driver vs element scope |
| `TableCell(header)` | shared XPath builder, document-scoped at page root | shared XPath builder, same axis rule | header lookup uses `ancestor::table[1]//tr[1]` |

### Implicit HTML roles (Selenium)

Playwright maps `WebRole` to Playwright's own ARIA role resolution, which understands implicit HTML roles. Selenium builds an XPath predicate instead, with its own mappings (`SeleniumLocatorTranslator.cs`):

- `Button` → `button`, `input[type=button|submit|reset]`, or `role='button'`
- `Link` → `a[@href]` or `role='link'`
- `Checkbox` / `Radio` → `input[type=checkbox|radio]` or the matching `role`
- `Textbox` → `textarea`, text-like inputs (no type, `text`, `email`, `password`, `tel`, `url`) or `role='textbox'`
- `Heading` → `h1`…`h6` or `role='heading'`
- `Image` → `img`; `Row` → `tr`; `Table` → `table`; `List` → `ul|ol`; `ListItem` → `li`; `Option` → `option`; `Combobox` → `select`; `RowGroup` → `tbody|thead|tfoot`
- `Dialog` → `dialog`; `Navigation` → `nav`; `ProgressBar` → `progress`; `Status` → `output`; `Searchbox`/`Slider`/`SpinButton` → `input[type=search|range|number]`
- `Grid` matches `role='grid'` only: a plain `table` is a `Table`, not a `Grid`, on both backends
- anything else → `@role='<lowercased name>'`

When a name is given, Selenium's predicate matches it against `aria-label`, `title`, `alt`, the normalized element text, or `value`, exact or `contains`, per the `exact` flag.

### Deepest-match text

Both backends match the deepest element carrying the text, not every ancestor. Playwright's `GetByText` does this by itself. Selenium's XPath adds `and not(.//*[predicate])`. Without it, `normalize-space(.)`, which includes descendant text, would make a banner's text match `body` and `html` too, and every single-element lookup would find several matches.

### Document versus element scope

A component root narrows the search. `By.TableCell*` is relative, so it widens to the whole document when there is no component around it:

- Playwright uses `th, td` at the page root, and `:scope > th, :scope > td` inside a component.
- Selenium uses `(//*[self::th or self::td])[n]` from the driver, and `./*[self::th or self::td][position()=n]` from an element.

The shared header lookup keeps the column's own header cells out of a document-wide search.

### Namespaced attributes

Selenium matches a namespaced attribute name literally, through `@*[name()='xml:lang']`. XPath's `@prefix:name` would need a namespace resolver that Selenium does not expose. In HTML the name is a plain attribute, and in XML it keeps its prefix, so `name()` covers both. Playwright escapes the name into a CSS attribute selector, and rejects names it cannot represent safely.

### Multiple matches

Single-element operations are strict on both backends. Playwright turns its strict-mode violation into `WebElementResolutionException`. Selenium's `ResolveSingle` throws `NoSuchElementException` for zero matches, and `WebElementResolutionException` for more than one.

## Next

- [Actions and assertions](./interactions.md) - using the elements these locators resolve.
- [Pages and components](./page-objects.md) - giving locators a readable component path.
