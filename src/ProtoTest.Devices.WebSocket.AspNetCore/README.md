# ProtoTest.Devices.WebSocket.AspNetCore

In-process WebSocket device connections for `ProtoTest.Devices`: reach an application's WebSocket
endpoint through its `TestServer`, with no listening socket.

```bash
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore
```

## Includes

- `AddInProcessWebSocketDevices<TProgram>("Api")`, registered once per (program, application); no client
  API changes.
- The same `AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")` then reaches the endpoint through
  the application's `TestServer` when it is hosted in-process, and over the configured address (socket)
  when it is published - one registration, every mode. A client is only routed through the transport of
  the application it was registered under, so multi-application suites with the same path stay apart.
- The registered `WebSocketDeviceOptions` (from either in-process or socket registration) are resolved
  and validated with the transport, and `ConnectTimeout` bounds the in-process connect too.
- Same frames, trace and coverage as the socket transport.

## Limits

- It only applies when the application is hosted in-process by the same suite
  (`AddAspNetCoreServer<TProgram>`); otherwise the client's address resolver runs, so a deployed
  environment needs no change here.
- One connection per device instance; an explicit `DisconnectAsync` releases it and the next send
  reconnects, while the test's end disconnects it either way.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
