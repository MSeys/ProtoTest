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

`WebKey` has `Enter`, `Tab`, `Escape`, `Space`, `Backspace`, `Delete`, `ArrowUp`, `ArrowDown`, `ArrowLeft`, `ArrowRight`, `Home`, `End`, `PageUp` and `PageDown`. Both backends read one shared `WebKeyMap`, so a key means the same thing on each.

```csharp
await page.Search.FillAsync("invoice");
await page.Search.PressAsync(WebKey.Enter);
await page.Language.SelectOptionAsync("nl");
await page.RememberMe.CheckAsync();
```

### How actions wait

- **Playwright** keeps its own auto-waiting: an action waits until the element is attached, visible, stable and enabled. ProtoTest resolves the locator and calls the native action; it never re-issues a failed one.
- **Selenium** retries for up to `ActionTimeout` (5 s by default), polling every `PollInterval`, until the element is displayed and enabled — and, for fills and selects, not read-only; for clicks and checks, not moving (when `WaitForStableBounds`) and not covered by another element (when `CheckClickObstruction` and the driver supports JavaScript). Otherwise it throws `WebActionabilityException` with the component path, locator and last observation.

To wait for something application-specific — a spinner, a pending XHR — add a [wait condition](./middleware.md#wait-conditions).

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

Assertions hang off `Should` (positive) and `ShouldNot` (negated), both returning a `WebAssertions`:

```csharp
public sealed class WebElement
{
    public WebAssertions Should { get; }      // the assertion must hold
    public WebAssertions ShouldNot { get; }   // the assertion must not hold
}

public sealed class WebAssertions
{
    ValueTask BeVisibleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    ValueTask BeEnabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    ValueTask BeCheckedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    ValueTask HaveTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    ValueTask ContainTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
    ValueTask HaveValueAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
```

```csharp
await page.Save.Should.BeEnabledAsync(TimeSpan.FromSeconds(2));
await page.Save.ClickAsync();
await page.Status.Should.HaveTextAsync("saved");
await page.Error.ShouldNot.BeVisibleAsync();
```

Assertions **poll**: every 50 ms until the condition holds or the timeout passes (**5 seconds** when you don't pass one). While polling, "element not found yet" and "not actionable yet" are treated as "not yet", not as failures. When time runs out you get a `WebAssertionException` describing the last thing observed.

- `HaveTextAsync` is an exact, ordinal comparison; `ContainTextAsync` checks for an ordinal substring. `ShouldNot` is the inverse of each.
- `HaveValueAsync` compares the value ordinally, but the failure message reports only the value's length.
- A timeout of zero or less throws `ArgumentOutOfRangeException`.

### Trace evidence

Each assertion is a `assert.web` operation on the element, with `web.expectation`, `web.assert.negated` and `web.assert.timeout` attributes. A passing assertion records a `web.page.verified` [coverage observation](./index.md#page-coverage) for the page it was checked on — that is what makes a page count as covered.

:::note[Form values stay out of the trace]
`FillAsync` records only the *length* of what was typed (`web.value` is `[REDACTED]`), and `HaveValueAsync` failures report the value's length rather than the value. Passwords and personal data you type in tests never end up in a `.prototrace` file.
:::

## Element metadata

```csharp
string Name { get; }                    // e.g. "Submit"
string ComponentPath { get; }           // e.g. "LoginPage.Form.Submit"
WebLocator Locator { get; }
WebElementReference Reference { get; }  // what WaitUntilAsync and custom waits consume
```

## Next

- [Flows](./flows.md) — group actions on a component into one named operation.
- [Diagnostics and artifacts](./diagnostics.md) — what a failed action leaves behind.
