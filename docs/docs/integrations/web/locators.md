---
sidebar_position: 3
title: Locators
description: "Find elements by role, label and text rather than CSS; each backend translates a WebLocator into its native query."
---

# Locators

`By` builds a `WebLocator` — a description of how to find something, translated by each backend into its native query. Prefer locators that describe what a user sees (roles, labels, text) over ones that describe markup (CSS); they survive redesigns and double as accessibility checks.

## The full list

```csharp
By.Role(WebRole role, string? name = null, bool exact = true)
By.Label(string value, bool exact = true)
By.Placeholder(string value, bool exact = true)
By.Text(string value, bool exact = false, bool ignoreCase = false)
By.TestId(string value)                     // data-testid
By.Attribute(string name, string value)
By.Css(string selector)
By.HasText(string value, bool exact = false, bool ignoreCase = false)   // filter — see below
By.At(WebLocator source, int index)         // zero-based
By.TableCell(string header, bool exact = true, bool ignoreCase = false)
By.TableCellNumber(int number)              // one-based
By.TableCellAt(int index)                   // zero-based
```

In order of preference:

| Locator | Use it for |
| --- | --- |
| `Role` | buttons, links, headings, checkboxes, rows — anything with an ARIA role and an accessible name |
| `Label` | form fields with a `<label>` |
| `Placeholder` | fields without a label (consider adding one) |
| `Text` | static text |
| `TestId` | elements with no good semantic handle; `data-testid="…"` |
| `Attribute` / `Css` | last resort |

`Attribute` only accepts names made of letters, digits, `-`, `_` and `:`.

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

`HasText` is **only valid as the right-hand side of `And`** — on its own, both backends throw `WebBackendCapabilityException`.

:::caution[Selenium limitations]
Selenium supports a narrower set of combinations: the right-hand side of `And` must be `HasText`, and the left-hand side can't be `By.Css`. Playwright accepts both.
:::

## Picking the nth match

```csharp
Element(By.At(By.Role(WebRole.Button, "Remove"), 2))   // the third "Remove" button
```

For repeated components, [`Components<T>()`](./page-objects.md#lists-of-components) with `At`, `Number`, `First` and `Matching` usually reads better.

## Table cells

Inside a `WebTableRow`, use the row's `Cell(...)`, `CellNumber(...)` and `CellAt(...)` methods rather than these locators directly — they're built on `By.TableCell*` and give the element a readable name.

## Describing a locator

Every locator can describe itself, and that description is what appears in traces and failure messages:

```csharp
By.Role(WebRole.Row).And(By.HasText("INV-123", exact: true)).Describe()
// Role(Row).And(HasText("INV-123", exact: true, ignoreCase: false))
```
