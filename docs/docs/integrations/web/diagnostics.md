---
sidebar_position: 9
title: Diagnostics and artifacts
description: "Screenshots, console output, page errors, failed requests and Playwright traces are captured automatically and attached to the failing test."
---

# Diagnostics and artifacts

A failing browser test shows what the browser saw at the failure. ProtoTest captures that automatically and attaches it to the test, where your [runner](../../runners/overview.md) and the [ProtoTrace viewer](../../observability/prototrace.md) both show it.

```text
web.click · LoginPage.Form.Submit                    # FAILED (WebActionabilityException)
├─ web.backend.execute · playwright · click          # the native driver call
├─ web-{session}-{element}-{n}-failure.png           # full-page screenshot
├─ web-{session}-{element}-{n}-page.html             # page HTML
└─ web-{session}-{element}-{n}-location.txt          # URL and title
```

Open what the test left: the `.prototrace` archive (see [Where it lands and how to open it](#where-it-lands-and-how-to-open-it)), or the screenshots and page HTML in the runner output next to the exception.

## On any failed operation

When an action or assertion fails, the backend captures the page at that moment:

| Artifact | File |
| --- | --- |
| Screenshot (full page) | `web-{session}-{element}-{n}-failure.png` |
| Page HTML | `web-{session}-{element}-{n}-page.html` |
| Location (URL and title) | `web-{session}-{element}-{n}-location.txt` |

ProtoTest lowercases the name parts and replaces anything but letters and digits with `-`. It replaces `{element}` with the operation name when the failure has no element. It numbers repeats with a per-test sequence, so a failure repeated on the same element keeps both sets of artifacts. Captures run in order screenshot, DOM, location.

A hand-written backend produces the same artifacts with `WebFailureArtifacts.CaptureAsync(…)` from `ProtoTest.Web`, and the same error wording with `WebBackendErrors`. Both are [backend-neutral building blocks](./index.md#compose).

Capturing never replaces the original error. Each artifact registers on its own, so one failing attachment does not drop the rest. If capturing fails, the trace shows `web.diagnostics.artifact_failed`, or `web.diagnostics.failed` when no attachment was produced at all, and the test still gets the real exception.

## Captured downloads

`WebSession.DownloadAsync`, and its [`WebPage` shortcut](./interactions.md#downloads), also keeps the downloaded file as a test attachment named `web-{session}-download-{n}-{file}`:

- The session and the file stem are cleaned up the same way as other artifact names.
- The number keeps two downloads with the same name apart.
- The extension is kept, so the file still opens, and the media type is guessed from it.

The attachment reaches your runner's output and the `.prototrace` archive like any other. A download that cannot be attached is traced as `web.download.attachment_failed`, and the test still gets the file. Selenium fails the call with `WebBackendCapabilityException` before the trigger runs, because the WebDriver protocol has no download API.

## Playwright traces

Playwright's own trace, a timeline with DOM snapshots you can open in the [Playwright Trace Viewer](https://trace.playwright.dev), is recorded while the test runs and kept according to `TraceRetention`:

| Value | Keeps the trace |
| --- | --- |
| `OnWebFailure` *(default)* | only when a web operation failed |
| `Always` | for every test |
| `Off` | never; tracing isn't started |

The kept trace is attached as `playwright-{session}-trace.zip`, with content type `application/vnd.microsoft.playwright.trace+zip`. A failure to capture it is traced as `web.playwright.trace_failed` and never replaces the test's own error.

With `CorrelateTraceGroups` on (the default), each ProtoTest operation is a named group in the Playwright trace, `[correlationId] [session] {name}`, so the two timelines line up.

- An operation nested inside another on the same session, such as a `WaitUntilAsync` predicate that reads an element, joins its caller's group instead of waiting for it.
- Groups run one at a time, and are skipped entirely when `TraceRetention = Off`.
- A failure to start or end a group is traced as `web.playwright.correlation_failed`.

### Browser signals

These go into ProtoTrace as events on the test, parented to the active operation via `web.correlation_id`:

| Option | Trace entry |
| --- | --- |
| `ConsoleCapture` (`WarningsAndErrors` by default) | `web.browser.console` carrying `browser.console.type` and `browser.console.text`, truncated at 4096 characters |
| `CapturePageErrors` | `web.browser.page_error` carrying `browser.error.message`, truncated at 4096 characters |
| `CaptureRequestFailures` | `web.browser.request_failed` carrying `http.method`, `http.url` without query or fragment, and `browser.request.failure`, truncated at 4096 characters |

## Selenium diagnostics

Selenium has no equivalent trace format, so ProtoTest writes its own `selenium-{session}-diagnostics.json`, kept according to `DiagnosticTraceRetention` (same three values, same default). The payload schema is `format = "prototest.selenium.diagnostics.v1"`:

| Field | |
| --- | --- |
| `format` | `"prototest.selenium.diagnostics.v1"` |
| `startedAtUtc`, `completedAtUtc` | the session's window |
| `driverType` | the concrete driver type |
| `url`, `title` | the final location, best-effort |
| `entries[]` | **every actionability attempt**: `TimestampUtc`, `Operation`, `ComponentPath`, `Element`, `Locator`, `Attempt`, `Outcome`, `Observation` and `ElapsedMilliseconds` |

When a Selenium click "randomly" fails, this is where you find out it was covered by a toast for 4.8 seconds. A failure while writing the attachment is traced as `web.diagnostics.artifact_failed`. This one JSON attachment is the whole timeline. There is no second report.

## What the trace records for every operation

Every web operation is a trace entry carrying `web.backend` and `web.session`. Element operations add `web.component`, `web.element`, `web.locator` and `web.component.roots`:

| Kind | For | Extra attributes |
| --- | --- | --- |
| `web.navigate` | `OpenAsync` | `web.address` |
| `web.click` | `ClickAsync` | |
| `web.fill` | `FillAsync` | `web.value = [REDACTED]`, `web.value.length` |
| `web.check` | `CheckAsync` / `UncheckAsync` | |
| `web.select_option` | `SelectOptionAsync` | `web.option` |
| `web.press` | `PressAsync` | `web.key` |
| `web.count` | `CountAsync` | |
| `web.read_text` / `web.read_value` | `TextAsync` / `ValueAsync` | |
| `web.is_visible` / `web.is_enabled` / `web.is_checked` | the state reads | |
| `assert.web` | every `Should`/`ShouldNot` assertion | `web.expectation`, `web.assert.negated`, `web.assert.timeout` |
| `web.flow` | [flows](./flows.md) | `web.flow.step_count` |
| `web.wait.until` | `WaitUntilAsync` | `web.expectation`, `web.wait.timeout` |
| `web.download` | [`DownloadAsync`](./interactions.md#downloads) | `web.download.requested_name`, `web.download.name`, `web.download.media_type`, `web.download.size`; registers the file as an attachment |
| `web.wait` | [wait conditions](./middleware.md) | `web.wait.timing`, `web.wait.condition`, `web.wait.timeout`, `web.wait.last_observed`, `web.operation` |
| `web.login` | [login](./login.md), setup phase | `web.login.persona`, `web.login.strategy` |
| `web.session.initialize` | the browser starting | `web.backend` |
| `web.session.complete` | the session closing, teardown phase | `web.backend` |

Inside each operation, a child `web.backend.execute` named `{backend} · {kind}` carries `web.correlation_id`, the phase, the outcome and any failure. It links the operation to the native driver call.

Coverage observations are the other half of the trace: `web.page.visited`, `web.page.verified` and `web.page.available`, each with `web.session` and `web.page.source` (`navigate`, `assert`, `vue-router` or `aspnetcore`). See [Page coverage](./index.md#page-coverage).

Other event kinds worth knowing when you read a trace: `web.page.discovery.failed` (Vue route discovery), `web.page.inventory.failed` (in-process ASP.NET Core inventory), `web.playwright.correlation_failed` / `web.playwright.trace_failed`, and `web.diagnostics.failed` / `web.diagnostics.artifact_failed` / `web.download.attachment_failed`.

## When artifacts are finalised

Web sessions complete during teardown, in reverse order: after the teardown hooks, before attachments are published and before the browser is disposed. The Playwright trace and the Selenium diagnostics are written then. A failure there is recorded on the session's `web.session.complete` entry and counts as a teardown failure, so by default the test fails. This order is what gets the native trace and diagnostics into the runner's output and the `.prototrace` archive.

## Where it lands and how to open it

Run the failing test, then open what it left:

- The `.prototrace` archive lands at `TestResults/prototest-{runId}.prototrace`, relative to the test process's working directory (usually the test project's `bin/<config>/<tfm>`), unless `ConfigureTracing` set `OutputPath`. Drop it on the [ProtoTrace viewer](https://trace.prototest.dev), or read it in a terminal with `prototest summary <file>` ([ProtoTrace](../../observability/prototrace.md)).
- Screenshots, page HTML, location files, the Playwright trace and the Selenium diagnostics are test attachments. NUnit, MSTest, xUnit v3 and TUnit show them in their own output. xUnit v2 writes each artifact to a file and prints the path ([runner limits](../../runners/overview.md)).
- The trace also records every artifact on the failing operation, so the viewer's failure view shows them next to the exception even when the runner's output was lost.

## Next

- [Overview](./index.md) - sessions, options and page coverage.
- [Waits and middleware](./middleware.md) - the `web.wait` entries in context.
