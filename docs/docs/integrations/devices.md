---
sidebar_position: 10
title: Devices
sidebar_label: Devices
description: "Talk to devices - a simulator or the real hardware - from the same test context, lifecycle and trace."
---

# Devices

`ProtoTest.Devices` talks to devices the way other integrations talk to APIs: a typed device class per kind of device, one instance per test, and every exchange in the same trace. The transport is a separate package - `ProtoTest.Devices.WebSocket` today - so the same suite runs against a simulator in CI and lab hardware on a bench.

```bash
dotnet add package ProtoTest.Devices
dotnet add package ProtoTest.Devices.WebSocket              # ws:// and wss:// endpoints
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore   # in-process endpoints, no socket
```

## Defining a device

Derive from `ProtoDevice` and give it the domain methods your scenario reads; the protected primitives (`ConnectAsync`, `SendAsync`, `ReceiveAsync`, `ExpectAsync`) carry the trace and coverage:

```csharp
public sealed class AcCharger : ProtoDevice
{
    public async ValueTask<string> BootAsync()
    {
        await SendTextAsync("BOOT");
        var ack = await ExpectAsync(
            "the charger acknowledges boot",
            frame => frame.TryGetText(out var text) && text == "BOOT_ACK");
        return ack.AsText();
    }
}
```

`ExpectAsync` is the assertion: it waits for the frame the behaviour depends on, contributes device coverage, and a timeout fails with the description and the frames exchanged so far.

## Registering

```csharp
builder.AddDevices(devices => devices
    .AddWebSocketClient("Chargers", address: "ws://localhost:9000", path: "/ocpp/{deviceId}")
        .AddDevice<AcCharger>()
        .AddProtocol<OcppProtocol>());
```

A client is declared once - its transport, how its addresses resolve, and the typed devices and protocol catalog that hang off it. There is no per-device configuration: the device id is passed to `For`, and `{deviceId}` in the address or path is filled from it. For anything more dynamic, pass a resolver instead of an address (`ProtoDeviceAddress.Template` and `.FromApplication` build the common ones).

Inside `AddApplication`, a client without an address uses the application's address, with `http(s)` becoming `ws(s)`, so the same suite follows the application from the simulator to staging:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddDevices(devices => devices
        .AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")
            .AddDevice<AcCharger>()));
```

An application hosted **in-process** has no listening socket. Register the in-process transport once and keep the same client registration: it uses the application's `TestServer` when the application is hosted in-process, and the configured address (socket) when it is published - so switching modes never touches the client:

```csharp
builder
    .AddInProcessWebSocketDevices<Program>("Api")
    .AddApplication("Api", app => app
        .AddAspNetCoreServer<Program>()                  // present in-process, absent when published
        .AddDevices(devices => devices
            .AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")
                .AddDevice<AcCharger>()));
```

## Using it

```csharp
[ProtoTest]
[RequiresDevice<AcCharger>]
public async Task A_charger_boots_and_acknowledges()
{
    var charger = Proto.Context.Devices("Chargers").For<AcCharger>("CP-001");
    await charger.BootAsync();
}
```

One instance per (client, type, id) and test, released with the test; a second `For` in the same test returns the same instance. `Devices()` without a name works when exactly one client is registered.

## Simulator or hardware

Nothing is configured per device id. `[RequiresDevice<TDevice>]` skips a test when the device type is not registered on any client, so a lab-only suite runs where the hardware is and skips elsewhere; point the application's address (or the client's resolver) at the simulator or the bench and the same tests run against either.

## Coverage with gaps

A matched `ExpectAsync` records the message kind its `IProtoDeviceProtocol` classifies. `DeviceCoverageCollector` reports every catalog entry no test asserted as a gap - the OpenAPI treatment for a device protocol:

```csharp
builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(
    new DeviceCoverageCollector("Devices", [new OcppProtocol()])));
```

## Transport options

The WebSocket backend takes code defaults in `AddWebSocketDevices(configure)` and lets configuration override them:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:WebSocket:ConnectTimeout` | how long a connection attempt may take | 10 s |
| `ProtoTest:Devices:WebSocket:ReceiveBufferBytes` | the buffer a receive reads into | 16 KB |
| `ProtoTest:Devices:WebSocket:KeepAliveInterval` | the keep-alive interval | unset |

## What the trace shows

A `device` entity per device (transport, address, connection state) and `device.connect`, `device.send`, `device.receive` and `device.command` operations; a failed expectation is a failed `device.command` carrying the awaited description and the frame log.

## Limits

- **One instance per (client, type, id) per test.** Devices are not shared across tests; state that must persist belongs to the product.
- **No per-device configuration.** The client's address template or resolver plus the device id is the whole story; a client is where environment differences live.
- **In-process endpoints are preferred automatically.** When `AddInProcessWebSocketDevices<TProgram>` is registered and the application is hosted in-process (`AddAspNetCoreServer`), the client uses its `TestServer`; otherwise the address resolver runs. The two transports are packages and registrations, not client API variants.
- **`ExpectAsync` consumes frames.** The bounded exchange log is for failure messages, not for matching a frame twice.
- **Replay is not shipped.** The `device.replay` operation is designed; recording and replaying a frame script against another transport is future work.
- **Transports ship one at a time.** WebSocket today; MQTT when a user needs it, TCP/serial after that.
- **The transport moves frames.** Protocol semantics - message kinds, sessions, OCPP operations - are the suite's code, and coverage only names what the catalog declares.
