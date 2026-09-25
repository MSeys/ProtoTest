namespace ProtoTest.Devices.Exceptions;

using ProtoTest.Core;

/// <summary>
/// An expectation on a device that was not met: what was awaited and the frames the device exchanged
/// until the timeout.
/// </summary>
public sealed class DeviceAssertionException : ProtoAssertionException
{
    public DeviceAssertionException(string message)
        : base(message)
    {
    }

    public DeviceAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
