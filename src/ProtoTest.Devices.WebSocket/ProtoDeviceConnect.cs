namespace ProtoTest.Devices.WebSocket;

/// <summary>
/// Compatibility alias for <see cref="ProtoTest.Devices.ProtoDeviceConnect"/>: the shared connect bound
/// lives in the devices core, so every transport depends on one home. The alias forwards its calls
/// unchanged and is removed in a future major.
/// </summary>
[Obsolete(
    "Use ProtoTest.Devices.ProtoDeviceConnect; this alias forwards to it and will be removed in a future major.")]
public static class ProtoDeviceConnect
{
    /// <summary>
    /// Forwards to <see cref="ProtoTest.Devices.ProtoDeviceConnect.WithTimeoutAsync{TConnection}"/>.
    /// </summary>
    public static Task<TConnection> WithTimeoutAsync<TConnection>(
        string target,
        TimeSpan timeout,
        Func<CancellationToken, Task<TConnection>> connect,
        CancellationToken cancellationToken)
        => global::ProtoTest.Devices.ProtoDeviceConnect.WithTimeoutAsync(target, timeout, connect, cancellationToken);
}
