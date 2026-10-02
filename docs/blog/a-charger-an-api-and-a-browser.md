---
slug: a-charger-an-api-and-a-browser
date: 2026-10-02
draft: true
image: /img/blog/a-charger-an-api-and-a-browser.png
title: Testing a charger, an API and a browser in one test
description: Devices that talk over WebSocket, MQTT, TCP or serial, in the same test as your API and your UI, with every frame in the trace.
authors: [mseys]
tags: [devices, iot, websocket, mqtt, testing]
---

import CommandBox from '@site/src/components/CommandBox';

Some systems do not end at an HTTP API. A charging backend talks to chargers over WebSockets. A metering platform
reads meters over MQTT. A gateway polls pumps over TCP, or a serial line on a bench. The bugs that matter live where
those worlds meet: the charger boots, and the dashboard still shows it as offline.

Those are the tests that are hardest to write, so they are often the ones nobody writes.

{/* truncate */}

## The test I wanted to write

When a charger boots, it should show as available, both in the API and on the page operators use. As a test:

```csharp
[Application("Api")]
[ProtoTest]
[RequiresDevice<AcCharger>]
public async Task ABootedChargerShowsAsAvailable()
{
    var charger = Proto.Context.Devices("Chargers").For<AcCharger>("CP-001");
    await charger.BootAsync();

    using var api = await Proto.Context.Rest()
        .GetAsync("/api/chargers/CP-001")
        .ExpectAsync(new { status = "Available" });

    var page = Proto.Context.Web().Page<ChargersPage>();
    await page.OpenAsync("/chargers");
    await page.Status.Should.HaveTextAsync("Available");
}
```

Three boundaries, one test, and no setup in the body. The WebSocket connection, the HTTP client, the browser and
their addresses are owned by the framework, set up before the test and closed after it.

## A device is a class

A device is a class with domain methods. The protected primitives send and expect frames, and record them:

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

`ExpectAsync` is the assertion. It waits for the frame the behaviour depends on, and when it times out it fails
with the description and every frame exchanged so far. That last part is what makes a failure readable: you see
what the charger sent and what came back, not "timeout after 5 s".

The test asks for a device by id. It connects on first use and disconnects when the test ends, so parallel tests
each get their own.

## Where the device connects

The client is declared once, with its transport and its address:

```csharp
builder.AddDevices(devices => devices
    .AddWebSocketClient("Chargers", address: "ws://localhost:9000", path: "/ocpp/{deviceId}")
        .AddDevice<AcCharger>()
        .AddProtocol<OcppProtocol>());
```

`{deviceId}` is filled in from the id the test passes to `For`. Declared inside an application, a client without
an address follows the application's address. When the application runs in-process, the in-process transport uses
its test server without opening a socket (register it once with `AddInProcessWebSocketDevices`); when it is
published, the same client uses the real socket. The tests do
not change.

The same model covers the other transports:

- **MQTT**, with a publish topic and a subscribe filter per client. The container package starts a Mosquitto broker
  for the run when none is configured.
- **TCP**, including a listening device for systems that dial their devices themselves.
- **Serial lines**, with framing, for hardware on a bench.

To start, add the core package and the transport you need:

<CommandBox
  title="Devices over WebSocket"
  commands={['dotnet add package ProtoTest.Devices', 'dotnet add package ProtoTest.Devices.WebSocket']}
/>

## Simulator in CI, hardware on the bench

Nothing is registered per device id. `[RequiresDevice<AcCharger>]` skips the test when no client serves that device
type. So the suite that runs against a simulator in CI can run against real hardware in the lab, by pointing the
address at it, and skips cleanly on a machine that has neither.

## Which messages did no test check?

For HTTP APIs, ProtoTest can report the endpoints no test called. Devices get the same thing for protocols. Give
the client a catalog of message kinds, and a coverage collector lists every kind no test expected:

```text
kind        status    count
PING_ACK    covered   1
PONG_ACK    gap       0  (no test asserted it)
```

For a protocol like OCPP, with dozens of message types, that list answers a question that is otherwise very hard
to answer: what does our suite never exercise?

## Everything lands in one trace

The frames, the HTTP request and response, the browser steps and the checks all go into the same `.prototrace`.
When the test fails, you open one run and read the whole story in order: the boot frame went out, the
acknowledgement came back, the API answered `Offline`, and the page never changed. That ordering is usually the
whole diagnosis.

## Limits

- **Devices are a preview** in 1.1. The surface can still change before 1.2.
- **The transport moves frames.** Protocol meaning, such as message kinds, sessions and OCPP operations, is your
  suite's code. Coverage names only what your catalog declares.
- **It does not simulate your hardware for you.** The device class is the simulator you write, or the client for
  the real device.

I built this while testing a real EV-charging backend, from the charger protocol to the operator's browser. The
[devices guide](/docs/integrations/devices) has every transport and option. If you test anything with a protocol
that is not HTTP, I would like to know whether this fits.
