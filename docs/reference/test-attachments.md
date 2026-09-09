# Test attachments

Integrations and tests can register text, binary data, or an existing file on the active execution context:

```csharp
Proto.Context.AddAttachment("response", json, "application/json");
Proto.Context.AddAttachment("screenshot", pngBytes, "image/png");
Proto.Context.AddAttachmentFile("logs/server.log", description: "Server log");
```

Core prefixes every attachment name with its numeric test ID, for example `123456-response`. Attachment names must then be unique within that test. Files are validated when registered. In-memory attachments are materialized to a GUID-qualified temporary file only for adapters whose native API requires a file, preventing collisions even across hosts that explicitly reuse an ID.

NUnit, MSTest, xUnit v3, and TUnit publish attachments through their native result APIs. xUnit v2 has no native attachment protocol; its adapter materializes the artifact and writes its path to test output.

Attachments created by after-test hooks and attributes are included. Publishing failures participate in the normal teardown error aggregation and do not prevent scope disposal.

## REST attachments

REST artifact capture is opt-in because request and response bodies can contain credentials or personal data:

```csharp
builder.AddRest(rest =>
{
    rest.CaptureAttachments(options =>
    {
        options.CaptureRequestBodies = true;
        options.CaptureResponses = true;
        options.CaptureExpectedShapes = true;
    });

    rest.AddClient("Api", "https://api.example.test");
});
```

Calling `CaptureAttachments()` without configuration enables all three artifact types. Every request receives a sequence-based name such as `123456-rest-01-response`, so multiple requests and tests can safely publish artifacts in the same suite.

`CaptureAttachments()` also binds the fixed `ProtoTest:Rest:Attachments` configuration section. No section path is passed by application code. The optional code callback defines suite defaults; configuration values override them so appsettings, environment variables, and CI settings remain authoritative:

```json
{
  "ProtoTest": {
    "Rest": {
      "Attachments": {
        "CaptureRequestBodies": false,
        "CaptureResponses": true,
        "CaptureExpectedShapes": true
      }
    }
  }
}
```
