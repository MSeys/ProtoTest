# Core concepts

This page is retained as a familiar entry point for existing links. For the focused reference pages, use:

- [Lifecycle](../reference/lifecycle.md) — suite/test ordering, hooks, attributes, and disposal.
- [Execution context](../reference/execution-context.md) — services, named clients, typed state, metadata, and coverage hits.
- [Extension points](../reference/extension-points.md) — which API to use when extending ProtoTest.

For practical usage, start with [writing tests](../guides/writing-tests.md), [context and state](../guides/context-and-state.md), or [hooks](../extending/hooks.md).

## The core idea

A framework adapter creates a ProtoTest host for the suite and a scoped `Proto.Context` for each test:

```text
suite setup
  -> run hooks
  -> create test context
  -> initialize clients
  -> before-test hooks and attributes
  -> test method
  -> after-test hooks and attributes
  -> dispose clients and context
```

You configure the host once, mark test methods with `[ProtoTest]`, and use `Proto.Context` for the services, clients, state, and integration helpers needed by the test.
