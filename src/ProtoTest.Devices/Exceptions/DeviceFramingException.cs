namespace ProtoTest.Devices.Exceptions;

/// <summary>
/// The bytes a device sent do not make a frame: the stream closed mid-frame, a frame outgrew the receive
/// limit, or a header announced an impossible length. The message names the address and the first bytes.
/// </summary>
public sealed class DeviceFramingException : InvalidOperationException
{
    public DeviceFramingException(string message)
        : base(message)
    {
    }

    public DeviceFramingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
