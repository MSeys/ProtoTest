# ProtoTest.Devices

> Preview: the surface can change before 1.2.

Talk to devices - simulators or hardware - from the same test context, lifecycle and trace. A suite
declares a named device client once and gets typed device instances per test by id.

```bash
dotnet add package ProtoTest.Devices
dotnet add package ProtoTest.Devices.WebSocket              # ws:// and wss:// endpoints
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore   # in-process endpoints, no socket
dotnet add package ProtoTest.Devices.Mqtt                   # MQTT publish/subscribe
dotnet add package ProtoTest.Devices.Mqtt.Testcontainers  # a Mosquitto broker owned by the run
dotnet add package ProtoTest.Devices.Tcp                    # raw TCP, connecting out or listening
dotnet add package ProtoTest.Devices.Serial                 # COM ports and /dev/tty lines
```

## Includes

- Named device clients declared once, mirroring the REST and GraphQL builders:
  `devices.AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}").AddDevice<AcCharger>()` or
  `devices.AddMqttClient("Meters", "meters/{deviceId}/out", "meters/{deviceId}/in").AddDevice<FlowMeter>()`.
- Typed device classes: derive from `ProtoDevice`, add domain methods, and compose the protected
  `ConnectAsync`, `SendAsync`, `ReceiveAsync` and `ExpectAsync` primitives.
- Per-test instances through `Proto.Context.Devices("Chargers").For<AcCharger>("CP-001")`, released
  with the test.
- Addresses resolve per test: an explicit template (`{deviceId}` filled in), a resolver, or the
  application's address when the client is registered inside `AddApplication`.
- Per-client transport settings through `WithSetting`, filled per device (`{deviceId}` included) and
  read by the transport through `DeviceEndpoint.Setting`; a credential belongs here, not in the traced
  address.
- Trace operations (`device.connect/send/receive/command`) and a `device` entity with client, type,
  transport, address and connection state; the release path disconnects the device and records it.
- Assertion-level coverage: a matched expectation records its message kind, and the client's protocol
  catalog reports kinds no test asserted as gaps.
- Stream framing for TCP, serial and custom stream transports: `DeviceFramers.Lines`, `Delimited`,
  `Enveloped`, `LengthPrefixed` and `FixedLength`, set per client with `WithFramer`, and
  `StreamDeviceConnection` for a stream of your own.
- Device messages as records: text (`DeviceMessage.Format`/`Parse`, prefix, separator or fixed widths)
  or binary (`[DeviceBinaryMessage]`, `ToBytes`/`FromBytes`), with `[DeviceChecksum<T>]` (NMEA,
  CRC-16/Modbus, XOR, sum) and `[DeviceFormat<T>]` field formatters (`DeviceFormatters`: scaled, hex,
  Unix time, BCD). `SendMessageAsync`/`ExpectMessageAsync` on the device, and `DeviceMessageProtocol`
  for coverage per message type.
- `ListenAsync` for transports the system under test connects to, such as `AddTcpListener`.
- `[RequiresDevice<TDevice>]` skips tests the environment cannot run.

## Limits

- One instance per (client, type, id) per test; devices are not shared across tests.
- One conversation per device instance: connect is single-flight, sends are serialized, one receive is
  in flight at a time, and a send racing a disconnect fails with a device error naming the device.
- An in-process endpoint needs `ProtoTest.Devices.WebSocket.AspNetCore`; the socket transport reaches
  published addresses. A client is only routed through its own application's in-process transport.
- `ExpectAsync` consumes frames; the bounded exchange log is for failure messages.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
- [Test time](https://prototest.dev/docs/foundation/time)
