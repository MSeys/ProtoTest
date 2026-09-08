# Extend ProtoTest

Most projects can use ProtoTest without writing extensions. When your suite needs reusable infrastructure or reporting, choose the extension point by the behavior you want to add.

## Choose an extension point

| I want to… | Use | Guide |
| --- | --- | --- |
| Start or stop shared infrastructure once per suite | `IProtoRunHook` | [Hooks](hooks.md) |
| Prepare or clean up every test | `IProtoTestHook` | [Hooks](hooks.md) |
| Apply behavior to selected classes or methods | `ProtoAttribute` | [Custom attributes and clients](custom-attributes-and-clients.md) |
| Create a named client for each test context | `IProtoClientInitializer` | [Custom attributes and clients](custom-attributes-and-clients.md) |
| Share typed scenario data | `IProtoContext` | [Context and state](../guides/context-and-state.md) |
| Aggregate execution events | `IProtoCollector` or `ProtoCollector` | [Coverage](coverage.md) |
| Export coverage items | `IProtoSink` | [Coverage](coverage.md) |
| Add services or configuration | `IProtoHostBuilder` | [Lifecycle reference](../reference/extension-points.md) |

## The usual extension sequence

1. Define the behavior and decide whether it is suite-wide, per-test, declarative, client-oriented, or reporting-oriented.
2. Implement the matching interface or base class.
3. Register it through `IProtoHostBuilder` or dependency injection.
4. Use `Proto.Context` for the active test's services, clients, and state.
5. Keep ownership explicit: clients registered in the test context are disposed by that context.

## Before extending

Check whether an existing integration already provides the behavior you need:

- [REST](../integrations/rest.md)
- [ASP.NET Core](../integrations/aspnetcore.md)
- [OpenAPI](../integrations/openapi.md)

If you are adding a new system-under-test integration, start with a client initializer and a test hook, then add target-specific request APIs and collectors as needed.

## Reference

See the [extension-points reference](../reference/extension-points.md) for the public interfaces and registration methods.
