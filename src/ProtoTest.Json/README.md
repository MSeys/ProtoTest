# ProtoTest.Json

Shared JSON handling for several ProtoTest integration packages.

This is mostly a transitive package.

## Includes

- The behind-the-scenes implementation for `ShouldMatchShape` in integrations such as REST and GraphQL.
- Sanitization of JSON results, including redaction of configured sensitive properties before they are added to tracing, attachments, etc.

## Learn more

- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- [Diagnostics showcase](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DiagnosticsShowcase.cs)
- [ProtoTest documentation](https://prototest.dev/)