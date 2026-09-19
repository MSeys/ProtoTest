---
sidebar_position: 4
title: Actions and assertions
description: "Click, type, select and assert on a WebElement; every action waits until the element is ready to take it."
---

# Actions and assertions

Everything you do to a page goes through a `WebElement`.

## Actions

```csharp
ValueTask ClickAsync(CancellationToken cancellationToken = default);
ValueTask FillAsync(string value, CancellationToken cancellationToken = default);
ValueTask CheckAsync(CancellationToken cancellationToken = default);
ValueTask UncheckAsync(CancellationToken cancellationToken = default);
ValueTask SelectOptionAsync(string value, CancellationToken cancellationToken = default);   // matches the option's value attribute
ValueTask PressAsync(WebKey key, CancellationToken cancellationToken = default);
```

`WebKey` has `Enter`, `Tab`, `Escape`, `Space`, `Backspace`, `Delete`, `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`, `Home`, `End`, `PageUp` and `PageDown`.

```csharp
await page.Search.FillAsync("invoice");
await page.Search.PressAsync(WebKey.Enter);
await page.Language.SelectOptionAsync("nl");
await page.RememberMe.CheckAsync();
```

### How actions wait

- **Playwright** uses its own auto-waiting: an action waits until the element is attached, visible, stable and enabled.
- **Selenium** retries for up to `ActionTimeout` (5 s by default) until the element is displayed and enabled — and, for fills and selects, not read-only; for clicks and checks, not moving and not covered by another element. Otherwise it throws `WebActionabilityException`.

To wait for something application-specific — a spinner, a pending XHR — add a [wait condition](./middleware.md).

## Reading state

```csharp
ValueTask<string> TextAsync(CancellationToken cancellationToken = default);
ValueTask<string?> ValueAsync(CancellationToken cancellationToken = default);
ValueTask<bool> IsVisibleAsync(CancellationToken cancellationToken = default);
ValueTask<bool> IsEnabledAsync(CancellationToken cancellationToken = default);
ValueTask<bool> IsCheckedAsync(CancellationToken cancellationToken = default);
```

These read **once**, right now. For checks in a test, prefer the assertions below — they retry.

## Assertions

```csharp
ValueTask ShouldBeVisibleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
ValueTask ShouldBeEnabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
ValueTask ShouldBeCheckedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
ValueTask ShouldHaveTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
ValueTask ShouldContainTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
ValueTask ShouldHaveValueAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
```

```csharp
await page.Save.ShouldBeEnabledAsync(TimeSpan.FromSeconds(2));
await page.Save.ClickAsync();
await page.Status.ShouldHaveTextAsync("saved");
```

Assertions **poll**: every 50 ms until the condition holds or the timeout passes (**5 seconds** when you don't pass one). While polling, "element not found yet" and "not actionable yet" are treated as "not yet", not as failures. When time runs out you get a `WebAssertionException` describing the last thing observed.

- `ShouldHaveTextAsync` is an exact, case-sensitive comparison.
- `ShouldContainTextAsync` checks for a substring.
- A timeout of zero or less throws `ArgumentOutOfRangeException`.

:::note[Form values stay out of the trace]
`FillAsync` records only the *length* of what was typed, and `ShouldHaveValueAsync` failures report the value's length rather than the value. Passwords and personal data you type in tests never end up in a `.prototrace` file.
:::

## Element metadata

```csharp
string Name { get; }            // e.g. "Submit"
string ComponentPath { get; }   // e.g. "LoginPage.Form.Submit"
WebLocator Locator { get; }
```
