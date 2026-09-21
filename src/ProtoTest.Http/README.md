# ProtoTest.Http

Shared HTTP functionality used by ProtoTest.Rest, ProtoTest.GraphQL and other HTTP-based integrations.

This is mostly a transitive package, but could be used in case you are building an additional HTTP-based integration for ProtoTest.

## Includes

- Shared registration and resolution of named HTTP clients and application targets.
- The `[Auth<T>]` attribute and built-in authenticators.
- Response buffering and sanitization of request and response information before it is recorded.

## Learn more

- [REST authentication](https://prototest.dev/docs/integrations/rest/authentication)
- [Integrations overview](https://prototest.dev/docs/integrations/overview)
- [ProtoTest documentation](https://prototest.dev/)
