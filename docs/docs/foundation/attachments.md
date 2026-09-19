---
sidebar_position: 8
title: Attachments
description: "Files a test produces reach your runner, so they show up next to the result in your IDE or CI, and land in the trace."
---

# Attachments

An attachment is a file a test produced: a response body, a screenshot, a Playwright trace, a report you generated. ProtoTest hands attachments to your runner — so they show up next to the test result in your IDE or CI — and bundles them into the [`.prototrace` archive](../observability/prototrace.md).

Integrations add attachments on their own ([REST](../integrations/rest/attachments.md), [GraphQL](../integrations/graphql/index.md), [Web](../integrations/web/diagnostics.md)). You can add your own from tests, hooks and attributes.

## Adding one

```csharp
Proto.Context.AddAttachment(
    "exported-invoice.csv",
    csv,
    mediaType: "text/csv",
    description: "The CSV the export endpoint returned");

Proto.Context.AddAttachment("thumbnail.png", pngBytes, "image/png");

Proto.Context.AddAttachmentFile("TestResults/invoice.pdf", mediaType: "application/pdf");
```

```csharp
ProtoTestAttachment AddAttachment(string name, string content, string mediaType = "text/plain", string? description = null);
ProtoTestAttachment AddAttachment(string name, ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachmentFile(string filePath, string? name = null, string mediaType = "application/octet-stream", string? description = null);
ProtoTestAttachment AddAttachment(ProtoTestAttachment attachment);
```

`AddAttachmentFile` throws `FileNotFoundException` if the file doesn't exist. The same three shapes are available as factories — `ProtoTestAttachment.FromText`, `FromBytes` and `FromFile` — when you want to build one before adding it.

## Names

- A name that has no `{testId}-` prefix is stored with one, so parallel tests never collide in the archive.
- A duplicate name — the prefixed result, compared case-insensitively — throws.
- In-memory content is materialized under `%TEMP%/ProtoTest/attachments/` when a runner needs a file path, with a file extension chosen from the media type.

## When they're published

Publishing happens during teardown, **after** every attribute and hook has finished and **before** `context.DisposeAsync` releases resources and the DI scope. So:

- an `AfterTestAsync` can still add attachments, and they'll be published;
- browser artifacts, which are finalised as the browser closes, are ready in time.

A failure publishing one attachment doesn't stop the others; it is recorded like the rest of the teardown failures and surfaces to the runner.

## How each runner receives them

| Runner | |
| --- | --- |
| NUnit | `TestContext.AddTestAttachment` |
| xUnit v3 | `TestContext.Current.AddAttachment` |
| MSTest | added to `TestResult.ResultFiles` |
| TUnit | `context.Output.AttachArtifact` |
| xUnit v2 | written to disk; the path is printed to the console |

## In the trace

An attachment is recorded on the **record axis**, not as an operation: `Trace.Attachment` adds an `attachment-{n}` item to the record of the operation that produced it, or to the test's orphans when there is no active operation. After the test, the content is copied into the archive under `resources/{testId}/{artifact-N}/{name}`, and the item is updated with its archive path and size — so the [viewer](../observability/prototrace.md) can open it right from the step that produced it. If capturing fails, the item carries the capture error instead of a path.

## Writing a runner integration

Runners provide an `IProtoTestAttachmentPublisher` when they start a test:

```csharp
public interface IProtoTestAttachmentPublisher
{
    ValueTask PublishAsync(ProtoTestAttachment attachment, CancellationToken cancellationToken = default);
}
```

`ProtoTestAttachment.ReadAllBytesAsync()` and `MaterializeFileAsync()` give you the content in whichever form your runner wants.

## Limits

- Attachment names are unique per test after prefixing; there is no overwrite.
- Attachments are in-memory or file-backed references until publishing; the archive copy is made after the test.
- Publishing is best-effort per attachment: one failure is reported, the rest still publish.
