# ProtoTest.Devices.Mqtt

MQTT transport for `ProtoTest.Devices`: talk to devices over publish/subscribe against a real broker -
Mosquitto in CI, the broker behind the lab.

```bash
dotnet add package ProtoTest.Devices.Mqtt
dotnet add package ProtoTest.Devices.Mqtt.Testcontainers   # a Mosquitto broker owned by the run
```

## Includes

- `AddMqttClient("Sensors", "sensors/{deviceId}/out", "sensors/{deviceId}/in")` inside `AddDevices`:
  the device publishes to one topic and receives what its subscribe filter matches, wildcards included.
  The topics ride the endpoint settings; a client registered directly with `AddClient` can name them in
  the address query instead, and a setting wins over the address parameter.
- The broker address comes from the registration, from `ProtoTest:Devices:Mqtt:Broker`, or from a
  `MosquittoBroker` container registered as a `UseContainer(...)` provider that fills the key for the run.
- Text and binary frames stay themselves: the frame's media type rides the MQTT 5 content type, and a
  text payload arrives as text.
- Connect timeout, keep-alive and maximum packet size as code defaults via the callback, overridden by
  `ProtoTest:Devices:Mqtt`.

## Limits

- **MQTT 5 over plain TCP.** The transport speaks `mqtt://`; an MQTT 3.1.1-only broker and `mqtts://`
  come later.
- **One broker connection per device instance.** The test's end disconnects it; an explicit
  `DisconnectAsync` releases it and the next send reconnects. One reader at a time: a second concurrent
  receive fails fast.
- **Topics are fixed at registration.** `{deviceId}` is the only placeholder; a client carries one
  publish topic and one subscribe filter. The filter may use the `+` and `#` wildcards, the publish
  topic may not.
- **Transport options are one set per run.** Connect timeout, keep-alive, the packet cap and a broker
  set through `configure` are shared by every MQTT client; a client that needs its own broker passes
  a resolver, because a configured or container broker wins over the registration's `address:`.
- **A missing broker fails the device, naming the key.** Without an address, a resolver or
  `ProtoTest:Devices:Mqtt:Broker`, creating the device fails instead of skipping - a device capability
  cannot see a broker configuration supplies later.
- **The transport moves frames only.** Message kinds, commands and coverage are the suite's protocol
  code (a catalog implements `IProtoDeviceProtocol`).

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
