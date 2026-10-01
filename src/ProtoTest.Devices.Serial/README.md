# ProtoTest.Devices.Serial

> Preview: the surface can change before 1.2.

Serial transport for `ProtoTest.Devices`: talk to devices on a COM port or a `/dev/tty` line, with the
same framing as the TCP transport.

```bash
dotnet add package ProtoTest.Devices.Serial
```

## Includes

- `AddSerialClient("Meters", DeviceFramers.Lines("\r\n"), address: "serial://COM3?baud=9600")`, or
  `serial:///dev/ttyUSB0?baud=115200&parity=even`. Settings: `baud`, `databits`, `parity`, `stopbits`,
  `handshake`; unset ones default to 9600 8N1 without handshake.
- Without an address the port comes from `ProtoTest:Devices:Serial:Ports:{client}`, and the client's
  devices exist only where that key is set: `[RequiresDevice<T>]` skips hardware tests elsewhere.
- Framing from `ProtoTest.Devices`; without a framer, a frame is a line of text ending in `\n`.

## Limits

- A port is open in one test at a time; tests that share a port must not run in parallel.
- Opening a missing or busy port fails the device's first use, naming the ports the machine has.
- The transport moves frames only: message kinds and coverage are the suite's protocol code.

## Learn more

- [Devices](https://prototest.dev/docs/integrations/devices)
