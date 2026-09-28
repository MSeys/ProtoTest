namespace ProtoTest.Devices;

using System.Globalization;

/// <summary>
/// Runs one device transport connect under its configured timeout - the rule every shipped device
/// transport shares: the attempt token is the timeout authority, so any failure that surfaces after it
/// fires - an <see cref="OperationCanceledException"/> or a transport that aborts the connect another
/// way - is reported as a <see cref="TimeoutException"/> naming the target. A caller's own cancellation
/// is never reclassified, and the caller keeps ownership of the connection it created.
/// </summary>
public static class ProtoDeviceConnect
{
    /// <summary>
    /// Connects through <paramref name="connect"/> with a linked token that expires after
    /// <paramref name="timeout"/>, so the transport call cannot outlive the connect bound. The
    /// connection the call returns is returned unchanged; a transport that owns its socket disposes it
    /// on failure itself.
    /// </summary>
    /// <typeparam name="TConnection">The transport's connection type.</typeparam>
    /// <param name="target">What the timeout message names: the address, or a path for an in-process endpoint.</param>
    /// <param name="timeout">The registered connect timeout.</param>
    /// <param name="connect">The transport's connect call, invoked with the attempt token.</param>
    /// <param name="cancellationToken">The caller's cancellation; it wins over the timeout.</param>
    /// <returns>The transport's connection.</returns>
    /// <exception cref="TimeoutException">The attempt token fired before the connect completed.</exception>
    public static async Task<TConnection> WithTimeoutAsync<TConnection>(
        string target,
        TimeSpan timeout,
        Func<CancellationToken, Task<TConnection>> connect,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(connect);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);
        try
        {
            return await connect(attempt.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (attempt.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Connecting to '{target}' timed out after {timeout.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s.",
                exception);
        }
    }
}
