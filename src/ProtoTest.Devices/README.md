# ProtoTest.Devices

Talk to devices - simulators or hardware - from the same test context, lifecycle and trace. A suite
declares a named device client once and gets typed device instances per test by id.

```bash
dotnet add package ProtoTest.Devices
dotnet add package ProtoTest.Devices.WebSocket              # ws:// and wss:// endpoints
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore   # in-process endpoints, no socket
```

## Includes

- Named device clients declared once, mirroring the REST and GraphQL builders:
  `devices.AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}").AddDevice<AcCharger>()`.
- Typed device classes: derive from `ProtoDevice`, add domain methods, and compose the protected
  `ConnectAsync`, `SendAsync`, `ReceiveAsync` and `ExpectAsync` primitives.
- Per-test instances through `Proto.Context.Devices("Chargers").For<AcCharger>("CP-001")`, released
  with the test.
- Addresses resolve per test: an explicit template (`{deviceId}` filled in), a resolver, or the
  application's address when the client is registered inside `AddApplication` - no per-device
  configuration.
- Trace operations (`device.connect/send/receive/command`) and a `device` entity with client,
  transport, address and connection state.
- Assertion-level coverage: a matched expectation records its message kind, and the client's protocol
  catalog reports kinds no test asserted as gaps.
- `[RequiresDevice<TDevice>]` skips tests the environment cannot run.

## Limits

- One instance per (client, type, id) per test; devices are not shared across tests.
- An in-process endpoint needs `ProtoTest.Devices.WebSocket.AspNetCore`; the socket transport reaches
  published addresses.
- `ExpectAsync` consumes frames; the bounded exchange log is for failure messages.
- Replay (`device.replay`) is designed but not shipped.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
- [Test time](https://prototest.dev/docs/foundation/time)
