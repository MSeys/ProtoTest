namespace ProtoTest.Web.Playwright;

/// <summary>
/// Reads one native trace file under the session's cap. Retention defaults to <c>OnWebFailure</c>, so
/// the big trace is the common case; the cap keeps teardown from buffering an unbounded zip, and the
/// teardown token lets a cancelled run stop the read.
/// </summary>
internal static class PlaywrightTraceFile
{
    /// <summary>
    /// Reads the trace file, or returns <see langword="null"/> when it exceeds
    /// <paramref name="maxBytes"/>. A non-positive cap reads without a bound.
    /// </summary>
    public static async Task<byte[]?> ReadAsync(string path, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maxBytes > 0 && new FileInfo(path).Length > maxBytes)
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }
}
