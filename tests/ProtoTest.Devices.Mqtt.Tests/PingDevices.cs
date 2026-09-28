namespace ProtoTest.Devices.Mqtt.Tests;

using ProtoTest.Devices;

/// <summary>The typed device a suite writes: domain methods over the protected protocol primitives.</summary>
internal sealed class PingDevice : ProtoDevice
{
    public ValueTask InitializeAsync() => ConnectAsync();

    public async ValueTask<string> PingAsync(string text = "PING")
    {
        await SendTextAsync(text);
        var frame = await ExpectAsync(
            $"the peer answers {text}",
            candidate => candidate.TryGetText(out var value) && value == $"{text}_ACK",
            TimeSpan.FromSeconds(15));
        return frame.AsText();
    }

    public async ValueTask<byte[]> EchoAsync(byte[] payload)
    {
        await SendAsync(DeviceFrame.Binary(payload));
        var frame = await ExpectAsync(
            "the peer echoes the bytes",
            candidate => candidate.Payload.Span.SequenceEqual(payload),
            TimeSpan.FromSeconds(15));
        return frame.Payload.ToArray();
    }

    public async ValueTask AwaitAsync(string expected, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        await ExpectAsync(
            $"the peer sends {expected}",
            candidate => candidate.TryGetText(out var value) && value == expected,
            timeout,
            cancellationToken);
    }

    public ValueTask SendText(string text) => SendTextAsync(text);
}

/// <summary>One side of the two-client round trip: pings and expects the answer.</summary>
internal sealed class CallerDevice : ProtoDevice
{
    public async ValueTask<string> PingAsync()
    {
        await SendTextAsync("PING");
        var frame = await ExpectAsync(
            "the responder answers the ping",
            candidate => candidate.TryGetText(out var value) && value == "PING_ACK",
            TimeSpan.FromSeconds(15));
        return frame.AsText();
    }
}

/// <summary>The other side: waits for the ping and answers it from its own client and topic.</summary>
internal sealed class ResponderDevice : ProtoDevice
{
    public ValueTask InitializeAsync() => ConnectAsync();

    public async ValueTask<string> AnswerAsync()
    {
        var frame = await ExpectAsync(
            "the caller pings",
            candidate => candidate.TryGetText(out var value) && value == "PING",
            TimeSpan.FromSeconds(15));
        await SendTextAsync("PING_ACK");
        return frame.AsText();
    }
}

/// <summary>The suite's protocol catalog: message kinds and how a frame classifies to one.</summary>
internal sealed class PingProtocol : IProtoDeviceProtocol
{
    public string Name => "Ping";

    public IReadOnlyList<DeviceProtocolEntry> Entries { get; } =
    [
        new("PING_ACK"),
        new("PONG_ACK")
    ];

    public string? Classify(DeviceFrame frame) => frame.TryGetText(out var text) ? text : null;
}
