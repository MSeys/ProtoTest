---
sidebar_position: 5
title: Flows
description: "Group steps on one component into a named flow that reads as one operation in the trace, with its steps nested beneath."
---

# Flows

A flow is a named sequence of steps on one component. It reads better than repeating the component on every line, and it shows up in the trace as **one** operation with the steps nested underneath — so a failed checkout reads as "`Checkout` › step 3 failed", not as a flat list of clicks.

```csharp
await checkout.Payment.Flow("Pay by card")
    .Fill(form => form.CardNumber, "4242 4242 4242 4242")
    .Fill(form => form.Expiry, "12/30")
    .Select(form => form.Country, "BE")
    .Check(form => form.SaveCard)
    .Click(form => form.Pay)
    .RunAsync();
```

Each lambda receives the component, so steps are type-checked and refactor-safe.

## Steps

```csharp
WebFlow<T> Fill(Func<T, WebElement> element, string value);
WebFlow<T> Click(Func<T, WebElement> element);
WebFlow<T> Check(Func<T, WebElement> element, bool isChecked = true);
WebFlow<T> Select(Func<T, WebElement> element, string value);
WebFlow<T> Press(Func<T, WebElement> element, WebKey key);
WebFlow<T> Do(Func<T, CancellationToken, ValueTask> interaction);
ValueTask RunAsync(CancellationToken cancellationToken = default);
```

`Check(..., isChecked: false)` unchecks. `Do` runs anything else — including assertions — as a step:

```csharp
await dialog.Flow("Confirm deletion")
    .Fill(d => d.Confirmation, "DELETE")
    .Do((d, ct) => d.Confirm.ShouldBeEnabledAsync(cancellationToken: ct))
    .Click(d => d.Confirm)
    .RunAsync();
```

## Rules

- Steps run **in order**, one at a time.
- Each step keeps its normal behaviour: actionability waits, [wait conditions](./middleware.md), middleware and its own trace entry.
- A flow runs **once**. Calling `RunAsync()` again, or adding a step after it started, throws `InvalidOperationException`.
- The flow's trace entry has kind `web.flow` and records the step count.

## A single named interaction

For one-off interactions that deserve a name in the trace, `InteractAsync` wraps an arbitrary delegate the same way:

```csharp
await page.Banner.InteractAsync("Dismiss cookie banner", banner => banner.Accept.ClickAsync());
```