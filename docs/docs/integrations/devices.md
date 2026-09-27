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

An application hosted **in-process** has no listening socket. Register the in-process transport once per application and keep the same client registration: it uses the application's `TestServer` when the application is hosted in-process, and the configured address (socket) when it is published - so switching modes never touches the client:

```csharp
builder
    .AddInProcessWebSocketDevices<Program>("Api")
    .AddApplication("Api", app => app
        .AddAspNetCoreServer<Program>()                  // present in-process, absent when published
        .AddDevices(devices => devices
            .AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")
                .AddDevice<AcCharger>()));
```

The registration is keyed by `(TProgram, application)`: a second application, or a second program, gets its own transport, and a client is only routed through the transport of the application it was registered under - two applications exposing the same path each serve their own clients. A client that has only a path (no address resolver) and no matching in-process transport fails naming the application instead of falling back to another transport.

A hand-written in-process transport applies `ProtoTestContextPropagation.ApplyTo(HttpRequest)` (from `ProtoTest.AspNetCore`) to the request it opens, so the handshake carries the test id and the application's clock bridge pushes the connecting test's clock, exactly like the in-process HTTP client. `ProtoDeviceConnect.WithTimeoutAsync` bounds the connect with the registered `ConnectTimeout` and reports an elapsed attempt as a `TimeoutException` naming the endpoint; both shipped WebSocket transports use it.

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

The WebSocket backend takes code defaults in `AddWebSocketClient(..., configure)` (or the same callback on `AddInProcessWebSocketDevices<TProgram>(application, configure)`) and lets configuration override them; the in-process
transport resolves and validates the same registered options, so a bad value fails when the device is
created and `ConnectTimeout` bounds the in-process connect too:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:WebSocket:ConnectTimeout` | how long a connection attempt may take | 10 s |
| `ProtoTest:Devices:WebSocket:ReceiveBufferBytes` | the buffer a receive reads into | 16 KB |
| `ProtoTest:Devices:WebSocket:KeepAliveInterval` | the keep-alive interval | unset |

## What the trace shows

A `device` entity per device (client, type, transport, address, connection state) and `device.connect`,
`device.send`, `device.receive` and `device.command` operations; a failed expectation is a failed
`device.command` carrying the awaited description and the frame log. The entity id is
`device:{client}:{deviceType}:{id}`, so two device types can share an id, and the test's end disconnects
the device: the final state is `device.connected = false` with a `device.disconnect` event, even when
the test never called `DisconnectAsync`.

## Limits

- **One instance per (client, type, id) per test.** Devices are not shared across tests; state that must persist belongs to the product.
- **One conversation per device instance.** Connection creation is single-flight and sends are serialized; one receive may be in flight at a time - a second concurrent receive fails fast naming the device instead of stealing frames. A send that races a disconnect fails with a device error naming the device. Send and receive may run concurrently.
- **No per-device configuration.** The client's address template or resolver plus the device id is the whole story; a client is where environment differences live.
- **In-process endpoints follow the application's winner.** When `AddInProcessWebSocketDevices<TProgram>(application)` is registered, the client uses the application's `TestServer` while the application's provider chain is served in-process (`UseInProcess<TProgram>()`, or `AddAspNetCoreServer` without a chain); when a configured, loopback or AppHost provider wins, the same registration declines and the socket at the winner's address serves it. The transport belongs to one `(TProgram, application)` pair, so multi-application suites route each client to its own application, and its `device` capability is declared only while the in-process winner can actually serve it.
- **`ExpectAsync` consumes frames.** The bounded exchange log is for failure messages, not for matching a frame twice.
- **Replay is not shipped.** The `device.replay` operation is designed; recording and replaying a frame script against another transport is future work.
- **Transports ship one at a time.** WebSocket today; MQTT when a user needs it, TCP/serial after that.
- **The transport moves frames.** Protocol semantics - message kinds, sessions, OCPP operations - are the suite's code, and coverage only names what the catalog declares.
