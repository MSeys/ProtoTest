---
sidebar_position: 8
title: Diagnostics and artifacts
---

# Diagnostics and artifacts

A failing browser test is only useful if you can see what the browser saw. ProtoTest captures that automatically and attaches it to the test, where your [runner](../../runners/overview.md) and the [ProtoTrace viewer](../../advanced/prototrace.md) both show it.

## On any failed operation

When an action or assertion fails, the backend captures the page at that moment:

| Artifact | Playwright | Selenium |
| --- | --- | --- |
| Screenshot | `web-{element}-failure.png` (full page) | `web-{session}-{element}-failure.png` |
| Page HTML | `web-{element}-page.html` | `web-{session}-{element}-page.html` |
| Location | `web-{element}-location.txt` (URL) | `web-{session}-{element}-location.txt` (URL and title) |

Capturing never replaces the original error. If capture itself fails, you'll see a `web.diagnostics.failed` or `web.diagnostics.artifact_failed` entry in the trace and still get the real exception.

## Playwright traces

Playwright's own trace — a timeline with DOM snapshots you can open in the [Playwright Trace Viewer](https://trace.playwright.dev) — is recorded while the test runs and kept according to `TraceRetention`:

| Value | Keeps the trace |
| --- | --- |
| `OnWebFailure` *(default)* | only when a web operation failed |
| `Always` | for every test |
| `Off` | never; tracing isn't started |

The kept trace is attached as `playwright-{session}-trace.zip`.

With `CorrelateTraceGroups` on (the default), each ProtoTest operation is a named group in the Playwright trace, so the two timelines line up.

### Browser signals

These go into ProtoTrace as events on the test:

| Option | Trace entry |
| --- | --- |
| `ConsoleCapture` (`WarningsAndErrors` by default) | `web.browser.console` — type and text, truncated at 4096 characters |
| `CapturePageErrors` | `web.browser.page_error` |
| `CaptureRequestFailures` | `web.browser.request_failed` — method, URL without query or fragment, failure reason |

## Selenium diagnostics

Selenium has no equivalent trace format, so ProtoTest writes its own `selenium-{session}-diagnostics.json`, kept according to `DiagnosticTraceRetention` (same three values, same default). It records the driver type, final URL and title, and **every actionability attempt** — operation, element, locator, attempt number, outcome, what was observed and how long it took. When a Selenium click "randomly" fails, this is where you find out it was covered by a toast for 4.8 seconds.

## What the trace records for every operation

Each web operation is a trace entry carrying the component path, element name, locator description, backend and session:

| Kind | For |
| --- | --- |
| `web.navigate` | `OpenAsync` — with the address |
| `web.click`, `web.check`, `web.select_option`, `web.press` | actions |
| `web.fill` | fills — the value is recorded as `[REDACTED]` with its length |
| `web.read_text`, `web.read_value`, `web.is_visible`, `web.is_enabled`, `web.is_checked`, `web.count` | reads |
| `assert.web` | assertions — with the expectation and timeout |
| `web.flow` | [flows](./flows.md) |
| `web.login` | [login](./login.md) |
| `web.wait` | [wait conditions](./middleware.md) |
| `web.session.initialize` / `web.session.complete` | the browser's lifetime |

## When artifacts are finalised

Browser artifacts are finalised during teardown **before** attachments are published and before the browser is closed — so the Playwright trace and Selenium diagnostics always make it into the runner's output and the `.prototrace` archive.
