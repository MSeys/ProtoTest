# ProtoTest.Devices.WebSocket

> Preview: the surface can change before 1.2.

WebSocket transport for `ProtoTest.Devices`: talk to devices over `ws://` or `wss://` - the simulator
or gateway in CI, the device's own endpoint in a lab.

```bash
dotnet add package ProtoTest.Devices.WebSocket
```

## Includes

- `AddWebSocketClient("Chargers", address: ..., path: "/ocpp/{deviceId}")` inside `AddDevices`, or no
  address at all when the client is registered inside `AddApplication` (the application's `http(s)`
  address becomes `ws(s)`).
- A resolver instead of the template when the address depends on the test or the device id.
- Text frames stay text, binary frames stay binary; close frames end the exchange with a clear error.
- Connect timeout, receive buffer and keep-alive code defaults via the callback, overridden by
  `ProtoTest:Devices:WebSocket`.

## Limits

- One WebSocket per device instance; an explicit `DisconnectAsync` releases it and the next send
  reconnects, and the test's end disconnects it either way. One reader at a time: a second concurrent
  receive fails fast.
- The transport opens a socket, so an in-process application's WebSocket endpoint needs
  `ProtoTest.Devices.WebSocket.AspNetCore`.
- The transport moves frames only: message kinds, commands and coverage are the suite's protocol code
  (a catalog implements `IProtoDeviceProtocol`).

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
