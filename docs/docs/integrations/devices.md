---
sidebar_position: 10
title: Test devices over WebSocket, MQTT, TCP or serial
sidebar_label: Devices
description: "Talk to devices over WebSocket, MQTT, TCP or a serial port, build their data strings as typed messages, and keep every exchange in the trace."
---

import SequenceLanes from '@site/src/components/SequenceLanes';

# Test devices over WebSocket, MQTT, TCP or serial
`ProtoTest.Devices` talks to devices the way other integrations talk to APIs: a typed device class per kind of device, one instance per test, and every exchange in the same trace.

```csharp
[ProtoTest]
[RequiresDevice<AcCharger>]
public async Task A_charger_boots_and_acknowledges()
{
    var charger = Proto.Context.Devices("Chargers").For<AcCharger>("CP-001");
    await charger.BootAsync();
}
```

Run it with `dotnet test`. A green run prints `Passed A_charger_boots_and_acknowledges`, and the trace records a `device` entity with `device.command` operations for the exchange.

## What it adds

- **Typed devices.** You write a class per kind of device, with domain methods such as `BootAsync`. A test asks for one by id.
- **One instance per test.** It connects on first use and disconnects when the test ends.
- **Every exchange in the trace**, with the frames sent and received.
- **Protocol coverage.** A catalog of message kinds reports the ones no test asserted.

- **Data strings as classes.** A record describes a message such as `$MTR,M-1,12.50*4B`, and the device sends and expects it by type.

The transport is a separate package: `ProtoTest.Devices.WebSocket` for WebSockets, `ProtoTest.Devices.Mqtt` for
publish/subscribe, `ProtoTest.Devices.Tcp` for raw sockets and `ProtoTest.Devices.Serial` for COM ports. So the same
suite runs against a simulator in CI and lab hardware on a bench.

## Install

```bash
dotnet add package ProtoTest.Devices
dotnet add package ProtoTest.Devices.WebSocket              # ws:// and wss:// endpoints
dotnet add package ProtoTest.Devices.WebSocket.AspNetCore   # in-process endpoints, no socket
dotnet add package ProtoTest.Devices.Mqtt                   # MQTT publish/subscribe
dotnet add package ProtoTest.Devices.Mqtt.Testcontainers    # a Mosquitto broker for the run
dotnet add package ProtoTest.Devices.Tcp                    # raw TCP, connecting out or listening
dotnet add package ProtoTest.Devices.Serial                 # COM ports and /dev/tty lines
```

## Compose

```csharp
builder.AddDevices(devices => devices
    .AddWebSocketClient("Chargers", address: "ws://localhost:9000", path: "/ocpp/{deviceId}")
        .AddDevice<AcCharger>()
        .AddProtocol<OcppProtocol>());
```

Declare each client once: its transport, its address, its settings, its device types and its protocol catalog. A
test passes the device id to `For`, and `{deviceId}` is filled in from it, in the address, the path and any setting.
For anything more dynamic, pass a resolver instead of an address. `ProtoDeviceAddress.Template` and
`.FromApplication` build the common ones.

### Settings

A client can carry settings for its transport. A hand-written transport names its own keys:

```csharp
// A transport the suite registered itself reads the settings it named.
devices.AddClient("Chargers", "MyTransport", resolveAddress: ProtoDeviceAddress.Template("memory://localhost:9000"))
    .WithSetting("model", "AC-{deviceId}")     // filled per device, like an address template
    .AddDevice<AcCharger>();
```

Settings go to the transport unchanged, with `{deviceId}` filled in per device. The trace never records them, so a
credential belongs in a setting rather than in the address. The MQTT transport uses this for its topics
(`publishTopic` and `subscribeTopic`).

### Following an application

Inside `AddApplication`, a client without an address uses the application's address, with `http(s)` becoming
`ws(s)`. The same suite then follows the application from the simulator to staging:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddDevices(devices => devices
        .AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")
            .AddDevice<AcCharger>()));
```

An application hosted **in-process** has no listening socket. Register the in-process transport once per
application, and keep the same client. It uses the application's `TestServer` while the application runs
in-process, and the socket at the configured address when it is published. Switching modes never touches the
client:

```csharp
builder
    .AddInProcessWebSocketDevices<Program>("Api")
    .AddApplication("Api", app => app
        .AddAspNetCoreServer<Program>()                  // present in-process, absent when published
        .AddDevices(devices => devices
            .AddWebSocketClient("Chargers", path: "/ocpp/{deviceId}")
                .AddDevice<AcCharger>()));
```

Each in-process transport belongs to one `(TProgram, application)` pair. A client is routed only through the
transport of its own application, so two applications with the same path each serve their own clients. A client
with only a path, and no matching in-process transport, fails naming the application instead of falling back to
another transport.

Every transport bounds its connect the same way: `ProtoDeviceConnect.WithTimeoutAsync` applies the registered
`ConnectTimeout`, and an attempt that runs out is a `TimeoutException` naming the endpoint.

### MQTT

`ProtoTest.Devices.Mqtt` talks to devices over publish/subscribe. A client declares one publish topic and one
subscribe filter, with `{deviceId}` filled in from the id passed to `For`. The broker is a simulator in CI, or the
real one behind the lab.

```csharp
builder.AddDevices(devices => devices
    .AddMqttClient("Meters", "meters/{deviceId}/out", "meters/{deviceId}/in")
        .AddDevice<FlowMeter>()
        .AddProtocol<FlowProtocol>());
```

- A send publishes to the publish topic. A receive returns the next message the subscribe filter matched, wildcards included.
- Text and binary frames keep their form. The frame's media type travels as the MQTT 5 content type.
- The topics are settings (`publishTopic` and `subscribeTopic`), so the trace records the broker address but not the topics.
- A client registered directly with `AddClient` may put both topics in the address query instead (`mqtt://host:port?publishTopic=…&subscribeTopic=…`). A setting wins over the address.

The broker address comes from `address:`, from a resolver, or from `ProtoTest:Devices:Mqtt:Broker`. The container
package starts a Mosquitto broker for the run and publishes its address under that key, so a client without an
address follows the run's broker:

```csharp
builder
    .AddInfrastructure("Mqtt", chain => chain
        .UseConfigured()
        .UseContainer(MosquittoBroker.Container()), MqttDeviceOptions.BrokerSetting)
    .AddDevices(devices => devices
        .AddMqttClient("Meters", "meters/{deviceId}/out", "meters/{deviceId}/in")
            .AddDevice<FlowMeter>());
```

A configured `ProtoTest:Devices:Mqtt:Broker`, the environment's broker, wins over the container, as in every
provider chain, and the same registration follows it.

`[RequiresDevice<TDevice>]` follows the broker too. A client with no address, resolver or broker key has no device
capability, so gated tests skip instead of failing when the device is created. An explicit `address:` or a
resolver comes from code, so the capability stays.

### TCP and serial

A raw socket or a serial line is a stream of bytes with no message boundaries. A framer cuts it into frames and
writes frames back:

| Framer | A frame is |
| --- | --- |
| `DeviceFramers.Lines("\r\n")` | the text before each terminator |
| `DeviceFramers.Delimited(delimiter)` | the bytes before each delimiter |
| `DeviceFramers.Enveloped(stx, etx)` | the bytes between a start and an end marker, after skipping noise before the start |
| `DeviceFramers.LengthPrefixed(2)` | a payload after its 1, 2 or 4 byte length |
| `DeviceFramers.FixedLength(16)` | exactly that many bytes |

A protocol with its own envelope, such as a checksum after the end marker, implements `IDeviceFramer`. A client
without a framer reads text lines ending in `\n`.

`ProtoTest.Devices.Tcp` connects devices out to `tcp://host:port`:

```csharp
builder.AddDevices(devices => devices
    .AddTcpClient("Meters", DeviceFramers.Lines("\r\n"), address: "tcp://127.0.0.1:7000")
        .AddDevice<FlowMeter>());
```

Inside `AddApplication`, pass a `port:` instead of an address, and the client uses the application's host.

`ProtoTest.Devices.Serial` opens a serial line:

```csharp
builder.AddDevices(devices => devices
    .AddSerialClient("Meters", DeviceFramers.Lines("\r\n"), address: "serial://COM3?baud=9600")
        .AddDevice<FlowMeter>());
```

The address names the port and its line settings: `serial:///dev/ttyUSB0?baud=115200&parity=even`. The settings
are `baud`, `databits`, `parity`, `stopbits` and `handshake`. Unset ones default to 9600 baud, 8 data bits, no
parity, one stop bit and no handshake.

A serial client without an address reads its port from `ProtoTest:Devices:Serial:Ports:{client}`. Its devices
exist only where that key is set, so `[RequiresDevice<TDevice>]` skips the hardware tests on a machine without the
device.

## The tasks

The `A_charger_boots_and_acknowledges` test above is the whole pattern: resolve the device for an id, call its domain method.

There is one instance per client, type, id and test, released with the test. A second `For` in the same test
returns the same instance. `Devices()` without a name works when exactly one client is registered.

Beyond that:

1. [Define a device class](#defining-a-device) with the domain methods your scenarios use.
2. [Let a test-side peer talk to an MQTT device](#a-conversation-with-a-test-side-peer).
3. [Write and read data strings as typed messages](#data-strings-as-messages).
4. [Let the system under test connect to the device](#a-device-the-system-connects-to).
5. [Run the same tests on a simulator or on hardware](#simulator-or-hardware).
6. [Report the message kinds no test asserted](#coverage-with-gaps).

### Defining a device

Derive from `ProtoDevice` and give it the domain methods your scenarios read. The protected primitives
(`ConnectAsync`, `SendAsync`, `ReceiveAsync`, `ExpectAsync`) record the trace and the coverage:

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

`ExpectAsync` is the assertion. It waits for the frame the behavior depends on and counts toward device coverage.
A timeout fails with the description and the frames exchanged so far.

### A conversation with a test-side peer

A second client with the topics swapped acts as the other side of an MQTT device. It publishes what the device
receives, and receives what the device publishes, so the whole conversation stays in the suite. Both clients use
the same `{deviceId}` and the run's broker:

<SequenceLanes
  participants={['Meter', 'Broker', 'Peer']}
  steps={[
    {from: 'Meter', to: 'Broker', label: <span>publishes <code>120</code> to <code>meters/M-001/out</code></span>},
    {from: 'Broker', to: 'Peer', label: <span>delivers <code>120</code> on <code>meters/M-001/out</code></span>},
    {from: 'Peer', to: 'Broker', label: <span>publishes a reply to <code>meters/M-001/in</code></span>},
    {from: 'Broker', to: 'Meter', label: <span>delivers the reply on <code>meters/M-001/in</code></span>},
  ]}
/>

```csharp
public sealed class FlowMeter : ProtoDevice
{
    public ValueTask PublishReadingAsync(string reading) => SendTextAsync(reading);

    public async ValueTask<string> AwaitReadingAsync()
    {
        var frame = await ExpectAsync(
            "the peer reads a flow reading",
            candidate => candidate.TryGetText(out _),
            TimeSpan.FromSeconds(5));
        return frame.AsText();
    }
}
```

```csharp
builder
    .AddInfrastructure("Mqtt", chain => chain
        .UseConfigured()
        .UseContainer(MosquittoBroker.Container()), MqttDeviceOptions.BrokerSetting)
    .AddDevices(devices => devices
        .AddMqttClient("Meters", "meters/{deviceId}/out", "meters/{deviceId}/in")
            .AddDevice<FlowMeter>()
        .AddMqttClient("Peer", "meters/{deviceId}/in", "meters/{deviceId}/out")
            .AddDevice<FlowMeter>());
```

```csharp
[ProtoTest]
[RequiresDevice<FlowMeter>]
public async Task A_meter_converses_with_a_test_side_peer()
{
    var meter = Proto.Context.Devices("Meters").For<FlowMeter>("M-001");
    var peer = Proto.Context.Devices("Peer").For<FlowMeter>("M-001");

    await meter.PublishReadingAsync("120");            // meters/M-001/out
    var reading = await peer.AwaitReadingAsync();      // Peer subscribes to meters/M-001/out

    Assert.That(reading, Is.EqualTo("120"));
}
```

### Data strings as messages

Many devices send fields in one line of text, such as `$MTR,M-1,12.50*4B`. Describe that line as a record, and
`DeviceMessage` writes and reads it:

```csharp
[DeviceMessage("$MTR")]
[DeviceChecksum<NmeaChecksum>]
public sealed record MeterReading(
    string MeterId,
    [DeviceField(Format = "0.00")] decimal Volume);

DeviceMessage.Format(new MeterReading("M-1", 12.5m));   // "$MTR,M-1,12.50*" plus the checksum
DeviceMessage.Parse<MeterReading>(text);                // the record, or a DeviceMessageFormatException
```

- **The fields are the record's parameters, in order.** On a class with settable properties, `[DeviceField(0)]`, `[DeviceField(1)]` and so on set the order.
- **`Separator`** sets the text between fields, a comma by default. An empty separator makes a fixed-width message. Every field then needs a `Width`, with `Pad` and `PadLeft` for zero-padded numbers.
- **`Format`** takes a .NET format for numbers, dates and times, `D` for an enum's number, or `Y|N` style words for a boolean. Values use the invariant culture. An empty field reads as `null` for a nullable field. A format without fractions, such as `yyyyMMddHHmmss`, drops them, so a parsed time is the whole second.
- **`[DeviceChecksum<T>]`** adds a checksum: `NmeaChecksum` (`*` and two hex digits) or your own `IDeviceMessageChecksum`.
- **`[DeviceFormat<T>]`** converts a value a format string cannot express. The formatter implements `IDeviceFieldFormatter<T>` for the field's type, and a mismatch fails when the message type is first used.
- **A mismatch names its cause:** the prefix, the field count, the checksum, or the field and value that would not parse.

A device sends and expects messages by type:

```csharp
public sealed class FlowMeter : ProtoDevice
{
    public async Task<MeterAck> ReportAsync(decimal volume)
    {
        await SendMessageAsync(new MeterReading(Id, volume));
        return await ExpectMessageAsync<MeterAck>(ack => ack.Accepted);
    }
}
```

`ExpectMessageAsync` skips frames that are not that message, and records the wait like `ExpectAsync`, named after
the message type. A test builds its data strings as objects, and a wrong field fails with the field's name.

#### Formatters

`DeviceFormatters` holds the conversions devices often need:

| Formatter | Field | Example |
| --- | --- | --- |
| `Tenths`, `Hundredths`, `Thousandths` | `decimal` | `12.50` sent as `1250` |
| `Hex` | `int` | `255` sent as `FF` |
| `UnixSeconds`, `UnixMilliseconds` | `DateTimeOffset` | seconds or milliseconds since 1970 |

```csharp
[DeviceMessage("PMP")]
public sealed record PumpReading(
    [DeviceFormat<DeviceFormatters.Hex>] int Pump,
    [DeviceFormat<DeviceFormatters.Hundredths>] decimal Flow,
    [DeviceFormat<DeviceFormatters.UnixSeconds>] DateTimeOffset At);   // "PMP,FF,1250,1790000000"
```

Another scale is one line, `sealed class Millivolts() : DeviceFormatters.ScaledDecimal(3);`. A device's own
encoding is a class that implements `IDeviceFieldFormatter<T>`.

#### Binary messages

`[DeviceBinaryMessage]` makes the same record a block of bytes: the prefix bytes, then each field at a fixed
size, big-endian unless `BigEndian = false`. A Modbus RTU request:

```csharp
[DeviceBinaryMessage(0x01, 0x03)]                 // slave 1, read holding registers
[DeviceChecksum<Crc16Modbus>]
public sealed record ReadHoldingRegisters(ushort Address, ushort Count);

DeviceMessage.ToBytes(new ReadHoldingRegisters(0, 1));   // 01 03 00 00 00 01 84 0A
```

- **Numbers** take their natural size, from `byte` to `double`. An enum takes its underlying type's size, and a `bool` one byte.
- **A string or byte array** needs `[DeviceField(Bytes = n)]`. A string is ASCII, padded with zero bytes.
- **Binary formatters** implement `IDeviceBinaryFieldFormatter<T>`: `ScaledInt16` and `ScaledInt32` for scaled registers, `BinaryUnixSeconds`, and `Bcd` for packed decimal digits. Derive one for your scale or width, such as `sealed class Centi() : DeviceFormatters.ScaledInt16(2);`.
- **Checksums** for binary messages are `Crc16Modbus`, `Xor8Checksum`, `Sum8Checksum` or your own `IDeviceBinaryChecksum`.
- **A mismatch names its cause:** the length, the prefix, the checksum, or the field and its bytes.

`SendMessageAsync` sends a binary message as a binary frame, and `ExpectMessageAsync` reads it back. With a TCP or
serial client, pair binary messages with a framer that delimits them, such as `DeviceFramers.FixedLength`.

#### Test data for devices

Message records are ordinary records, so [ProtoTest.Data](./data/defaults.md) builds them. A defaults module fills
what every reading shares, and the test sets only what it is about:

```csharp
builder.AddData(data => data.For<MeterReading>()
    .Default(reading => reading.MeterId, context => $"M-{context.ObjectSequence}")
    .Default(reading => reading.At, context => context.Clock.GetUtcNow()));

var reading = Proto.Context.Data().For<MeterReading>().With(r => r.Volume, 12.5m).Build();
var series = Proto.Context.Data().For<MeterReading>().BuildMany(100);   // 100 distinct meters
await meter.SendMessageAsync(reading);
```

The default stamp reads the test's clock, so `Proto.Context.Clock.Advance` moves it and a run repeats exactly.

#### Coverage per message type

`DeviceMessageProtocol` turns message types into a protocol catalog. Each type is one message kind, and a matched
`ExpectMessageAsync` covers it:

```csharp
var pumps = new DeviceMessageProtocol("Pump protocol").Add<PumpReading>().Add<PumpAlarm>();
builder.AddDevices(devices => devices
    .AddTcpClient("Pumps", address: "tcp://127.0.0.1:7000")
        .AddProtocol(pumps)
        .AddDevice<Pump>());
builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(
    new DeviceCoverageCollector("Pumps", [pumps])));
```

The report lists every message type no test expected as a gap.

### A device the system connects to

Some systems dial their devices: a backend that polls meters, or a gateway that opens the connection. Register a
listening client, and each device binds its own port:

```csharp
builder.AddDevices(devices => devices
    .AddTcpListener("Meters", DeviceFramers.Lines("\r\n"))
        .AddDevice<FlowMeter>());
```

`ListenAsync` returns the address with the port the operating system chose. Give the device class a method that
exposes it, and hand the address to the application:

```csharp
public sealed class FlowMeter : ProtoDevice
{
    public ValueTask<string> OpenPortAsync() => ListenAsync();
}

var meter = Proto.Context.Devices("Meters").For<FlowMeter>("M-001");
var address = await meter.OpenPortAsync();                  // tcp://127.0.0.1:53817
await RegisterMeterAsync(address);                          // however the application learns its devices
var request = await meter.AwaitRequestAsync();              // waits for the application to connect
```

The first send or receive waits for the connection, up to `AcceptTimeout`. Each device has its own port, so
parallel tests never share one.

### Simulator or hardware

Nothing is registered per device id. `[RequiresDevice<TDevice>]` skips a test when no client registers that device
type, so a lab-only suite runs where the hardware is and skips elsewhere. Point the application's address, or the
client's resolver, at the simulator or the bench, and the same tests run against either.

## Coverage with gaps

A matched `ExpectAsync` records the message kind its `IProtoDeviceProtocol` recognizes. `DeviceCoverageCollector`
reports every kind in the catalog that no test asserted as a gap, as the OpenAPI integration does for endpoints:

```csharp
builder.ConfigureServices(services => services.AddSingleton<IProtoCollector>(
    new DeviceCoverageCollector("Chargers", [new OcppProtocol()])));
```

The collector's name is the device client's name, because each matched expectation is recorded under its client.

A report row names the message kind, whether a test asserted it, and how often:

```text
kind        status    count
PING_ACK    covered   1
PONG_ACK    gap       0  (no test asserted it)
```

## Transport options

Each transport takes code defaults, and configuration overrides them. Bad values fail when the device is created.

The WebSocket transport takes its defaults in `AddWebSocketClient(..., configure)`, or the same callback on
`AddInProcessWebSocketDevices<TProgram>(application, configure)`. The in-process transport uses the same options,
so `ConnectTimeout` bounds its connect too:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:WebSocket:ConnectTimeout` | how long a connection attempt may take | 10 s |
| `ProtoTest:Devices:WebSocket:ReceiveBufferBytes` | the buffer a receive reads into | 16 KB |
| `ProtoTest:Devices:WebSocket:KeepAliveInterval` | the keep-alive interval | unset |

The MQTT transport takes its defaults in `AddMqttClient(..., configure)`:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:Mqtt:Broker` | the broker address a client without one resolves | unset |
| `ProtoTest:Devices:Mqtt:ConnectTimeout` | how long a connection attempt may take | 10 s |
| `ProtoTest:Devices:Mqtt:KeepAlivePeriod` | the keep-alive interval sent to the broker | library default |
| `ProtoTest:Devices:Mqtt:MaxPacketBytes` | the largest MQTT packet the client accepts, offered to the broker as MQTT 5 `MaximumPacketSize`; 0 reads without a cap | 4 MiB |

The TCP transports take their defaults in `AddTcpClient(..., configure)` or `AddTcpListener(..., configure)`:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:Tcp:ConnectTimeout` | how long a connection attempt may take | 10 s |
| `ProtoTest:Devices:Tcp:AcceptTimeout` | how long a listening device waits for the application to connect | 30 s |
| `ProtoTest:Devices:Tcp:MaxFrameBytes` | the most bytes a receive buffers without completing a frame | 1 MiB |
| `ProtoTest:Devices:Tcp:NoDelay` | send small writes at once instead of batching them | true |

The serial transport takes its defaults in `AddSerialClient(..., configure)`:

| Key | Meaning | Default |
| --- | --- | --- |
| `ProtoTest:Devices:Serial:Ports:{client}` | the port address of a client registered without one | unset |
| `ProtoTest:Devices:Serial:MaxFrameBytes` | the most bytes a receive buffers without completing a frame | 1 MiB |

## In the trace and coverage

```text
device.command · the peer reads a flow reading
├─ device:Meters:FlowMeter:M-001 (client, type, transport, address, connection state)
├─ predicate + timeout 5s; matched frame → message kind recorded for coverage
└─ timeout fails with the description and the frames exchanged so far
```

- **A `device` entity per device**, with its client, type, transport, address and connection state. Its id is `device:{client}:{deviceType}:{id}`, so two device types can share an id.
- **Operations** `device.connect`, `device.send`, `device.receive` and `device.command`. A failed expectation is a failed `device.command`, with the awaited description and the frame log.
- **A `device.listen` event** for a listening device, with the address it handed out.
- **A disconnect at the end of the test.** The final state is `device.connected = false`, with a `device.disconnect` event, even when the test never called `DisconnectAsync`.

## Limits

- **One instance per client, type, id and test.** Devices are not shared across tests. State that must last belongs to the product.
- **One conversation per device instance.** Connecting happens once and sends run one at a time. One receive may be waiting at a time: a second fails at once, naming the device, instead of stealing frames. A send and a receive may run together. A send that races a disconnect fails with a device error.
- **Configuration is per client.** The address template or resolver, the settings and the device id are all there is. The transport options are shared by the whole run.
- **In-process endpoints follow the application's provider chain.** With `AddInProcessWebSocketDevices<TProgram>(application)` registered, the client uses the `TestServer` while the in-process provider (`UseInProcess<TProgram>()`, or `AddAspNetCoreServer` without a chain) serves the application. When a configured, loopback or AppHost provider wins, the in-process transport declines, and the socket at that address serves the client. Its `device` capability exists only while the in-process provider can serve it.
- **`ExpectAsync` consumes frames.** The exchange log is for failure messages, not for matching a frame twice.
- **Four transports ship.** WebSocket, MQTT, TCP and serial. There is no TLS for TCP: `tcp://` only.
- **A listening device accepts one connection.** It stops listening once the application connected. An application that reconnects needs a new device in a new test.
- **A serial port is open in one test at a time.** Tests that share a port must not run in parallel.
- **A stream frame has a size limit.** A receive that buffers more than `MaxFrameBytes` without a whole frame fails and shows the first bytes. That usually means the framer does not match the device.
- **A message has a fixed list of fields.** Repeated groups and optional trailing fields need a string field, or a class per variant. A text field holds a string, number, boolean, enum, date or time, or uses a formatter.
- **A binary message has a fixed length.** Every field has a fixed size, so a length byte that announces a variable payload needs a framer and a byte array field per size.
- **MQTT speaks MQTT 5 over plain TCP.** `mqtt://` only. An MQTT 3.1.1-only broker and `mqtts://` fail the connect.
- **An MQTT client has one publish topic and one subscribe filter.** `{deviceId}` is the only placeholder, so one client covers one topic convention, and two device families need two clients. The filter may use the `+` and `#` wildcards; the publish topic may not.
- **MQTT options are one set per run.** Connect timeout, keep-alive, the packet cap and a broker set through `configure` apply to every MQTT client. A client that needs its own broker passes a resolver, because a configured or container broker wins over `address:`.
- **A missing MQTT broker removes the device capabilities.** A client without an address, a resolver or a broker set through `configure` depends on `ProtoTest:Devices:Mqtt:Broker`. `[RequiresDevice<TDevice>]` skips while nothing provides that key. An ungated test still fails when the device is created, naming the key.
- **The MQTT broker is shared.** One broker serves the run, and a parallel suite, so tests use their own topic namespace. ProtoTest neither leaves topics behind nor cleans any up.
- **The transport moves frames.** Protocol meaning, such as message kinds, sessions and OCPP operations, is the suite's code. Coverage names only what the catalog declares.

## Writing a transport

A transport over a byte stream, such as a Bluetooth socket or a USB bridge, wraps its stream in
`StreamDeviceConnection` with the client's framer (`DeviceEndpoint.Framer`). Framing, the size limit and the
mid-frame errors then match the TCP and serial transports.

A hand-written in-process transport applies `ProtoTestContextPropagation.ApplyTo(HttpRequest)` (from `ProtoTest.AspNetCore`) to the request it opens. The handshake then carries the test id, and the application's clock bridge uses the connecting test's clock, as with the in-process HTTP client.
