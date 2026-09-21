# ProtoTest.Core

This is the part every ProtoTest integration builds on.

Most test projects get this package through a runner adapter or another ProtoTest package. Install it directly when building an integration or runner adapter of your own.

## Why does Core exist?

I did not want every integration to manage setup, cleanup and tracing in its own way.

Core provides one host for the test run and one `ProtoExecutionContext` for each test. Integrations use that context for clients, typed contexts, resources and attachments.

It also owns the shared lifecycle. Setup, test execution and teardown can all be written to the same `.prototrace` file.

## Learn more

- [Foundation overview](https://prototest.dev/docs/foundation/overview)
- [Extending ProtoTest](https://prototest.dev/docs/advanced/extending)
- [ProtoTest documentation](https://prototest.dev/)
