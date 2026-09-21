# ProtoTest.Testcontainers

Shared container-resource support used by ProtoTest's Testcontainers packages.

This is mostly a transitive package. Use it directly when building another run-scoped container integration.

`ProtoContainerResource<TContainer>` handles start-once and release-once behaviour. Register a resource with `AddInfrastructure(...)` when the host should start it and expose values such as a connection string.

The package does not depend on a specific Testcontainers module.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [Extending ProtoTest](https://prototest.dev/docs/advanced/extending)
- [Package source](https://github.com/MSeys/ProtoTest/tree/main/src/ProtoTest.Testcontainers)
