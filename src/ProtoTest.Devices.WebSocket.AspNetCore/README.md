# ProtoTest.Devices.WebSocket.AspNetCore

In-process WebSocket device connections for `ProtoTest.Devices`: reach an application's WebSocket
endpoint through its `TestServer`, with no listening socket.

```bash
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore
```

## Includes

- `AddInProcessWebSocketDevices<TProgram>("Api")`, registered once per host; no client API changes.
- The same `AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")` then reaches the endpoint through
  the application's `TestServer` when it is hosted in-process, and over the configured address (socket)
  when it is published - one registration, every mode.
- Same frames, trace and coverage as the socket transport.

## Limits

- It only applies when the application is hosted in-process by the same suite
  (`AddAspNetCoreServer<TProgram>`); otherwise the client's address resolver runs, so a deployed
  environment needs no change here.
- One connection per device; reconnect means a new test.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
