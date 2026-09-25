namespace ProtoTest.Devices;

using System.Text;

/// <summary>
/// One device frame, as bytes plus a media type. OCPP is text JSON, MQTT payloads are binary, so bytes
/// are the neutral unit; the text helpers cover the common protocol.
/// </summary>
public readonly record struct DeviceFrame(ReadOnlyMemory<byte> Payload, string MediaType = "text/plain")
{
    /// <summary>Creates a text frame, UTF-8 encoded.</summary>
    public static DeviceFrame Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new DeviceFrame(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Creates a binary frame.</summary>
    public static DeviceFrame Binary(ReadOnlyMemory<byte> payload, string mediaType = "application/octet-stream")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        return new DeviceFrame(payload, mediaType);
    }

    /// <summary>Gets the payload as text; the payload must be text.</summary>
    public string AsText()
    {
        if (!MediaType.StartsWith("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The frame is '{MediaType}', not text; use Payload or ReadAsBinary.");
        }

        return Encoding.UTF8.GetString(Payload.Span);
    }

    /// <summary>Gets the payload as text when the frame is textual.</summary>
    public bool TryGetText(out string text)
    {
        if (MediaType.StartsWith("text", StringComparison.OrdinalIgnoreCase))
        {
            text = Encoding.UTF8.GetString(Payload.Span);
            return true;
        }

        text = string.Empty;
        return false;
    }

    /// <summary>A short form for trace attributes and failure messages.</summary>
    public override string ToString()
        => TryGetText(out var text) ? text : $"{Payload.Length} bytes of {MediaType}";
}
