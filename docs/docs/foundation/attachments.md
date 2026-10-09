---
sidebar_position: 12
title: Attachments
description: "Files a test produces reach your runner, so they show up next to the result in your IDE or CI, and land in the trace."
---

# Attachments

An attachment is a file a test produced, for example a response body or a screenshot. ProtoTest hands attachments to your runner, so they show up next to the test result in your IDE or CI. It also bundles them into the [`.prototrace` archive](../observability/prototrace.md).

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

`AddAttachmentFile` throws `FileNotFoundException` if the file does not exist. The same three shapes are available as factories, `ProtoTestAttachment.FromText`, `FromBytes` and `FromFile`, when you want to build one before adding it.

## Names

- A name that has no `{testId}-` prefix is stored with one, so parallel tests never collide in the archive.
- A duplicate name, compared case-insensitively after prefixing, throws.
- In-memory content is materialized under `%TEMP%/ProtoTest/attachments/` when a runner needs a file path, with a file extension chosen from the media type.

## When they are published

Publishing happens during teardown, **after** every attribute and hook has finished and **before** `context.DisposeAsync` releases resources and the DI scope. So:

- an `AfterTestAsync` can still add attachments, and they are published.
- browser artifacts, which are finalised as the browser closes, are ready in time.

A failure publishing one attachment does not stop the others. It is recorded like the rest of the teardown failures, and by default that fails the test. See [Lifecycle](./lifecycle.md#a-test).

## How each runner receives them

| Runner | Receives them through |
| --- | --- |
| NUnit | `TestContext.AddTestAttachment` |
| xUnit v3 | `TestContext.Current.AddAttachment` |
| MSTest | added to `TestResult.ResultFiles` |
| TUnit | `context.Output.AttachArtifact` |
| xUnit v2 | written to disk, with the path printed to the console |

## What the trace shows

An attachment is recorded on the **record axis**, not as an operation. `Trace.Attachment` adds an `attachment-{n}` item to the record of the operation that produced it, or to the test's orphans when there is no active operation. After the test, the content is copied into the archive under `resources/{testId}/{artifact-N}/{name}`. The item is then updated with its archive path and size, so the [viewer](../observability/prototrace.md) can open it from the step that produced it. If capturing fails, the item carries the capture error instead of a path.

One journey's archive, the sample suite's project journey:

```text
l1-first-journey.prototrace
  resources/117492000001/artifact-1..4    request, response, expected shape, scenario summary
  resources/run/JsonReportSink, HtmlReportSink    report.json, report.html
  sources/1..2, manifest.json, spans.json, state.json
```

Record against operation in that archive:

```text
[http.request POST /api/v1/projects] <- attachment-1 request, attachment-2 response
[assert.json.shape]                  <- attachment-3 expected shape
[orphans: no active operation]       <- attachment-4 scenario summary, published in teardown
```

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

- Attachment names are unique per test after prefixing. There is no overwrite.
- Attachments are in-memory or file-backed references until publishing. The archive copy is made after the test.
- Publishing is best-effort per attachment. One failure is reported, and the rest still publish.
- **A file-backed runner (NUnit, TUnit) makes an in-memory attachment durable** by writing a copy under `%TEMP%/ProtoTest/attachments`. ProtoTest does not delete those copies, so they accumulate until the OS temp cleaner runs. They are safe to delete between runs.
- The archive embeds attachment bytes by default. `trace.EmbedArtifacts = false` declares them (name, media type, size) without reading or writing the content, and `MaxArtifactBytes` caps any single artifact.
