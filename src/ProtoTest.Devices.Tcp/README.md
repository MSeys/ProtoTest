# ProtoTest.Devices.Tcp

> Preview: the surface can change before 1.2.

TCP transport for `ProtoTest.Devices`: talk to devices over a raw socket, the way most devices that send
data strings do.

```bash
dotnet add package ProtoTest.Devices.Tcp
```

## Includes

- `AddTcpClient("Meters", DeviceFramers.Lines("\r\n"), address: "tcp://127.0.0.1:7000")`: the device
  connects out, to the system under test, a gateway or a simulator.
- `AddTcpListener("Meters", DeviceFramers.Lines("\r\n"))`: the system under test connects to the device.
  Each device binds its own port, and `ListenAsync` returns the address to hand the application.
- Framing from `ProtoTest.Devices`: lines, delimited, STX/ETX envelopes, length-prefixed and fixed-length
  frames, or your own `IDeviceFramer`. Without one, a frame is a line of text ending in `\n`.
- Connect and accept timeouts, a frame size limit and `NoDelay`, overridden by `ProtoTest:Devices:Tcp`.

## Limits

- No TLS: `tcp://` only.
- A listening device accepts one connection, then stops listening.
- The transport moves frames only: message kinds and coverage are the suite's protocol code.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
