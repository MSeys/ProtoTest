namespace ProtoTest.Core;

using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;

/// <summary>What a readiness wait observed, for the trace evidence and failure messages.</summary>
public sealed record ProtoReadinessResult(int Attempts, TimeSpan Waited, string? LastError);

/// <summary>The readiness checks and the wait loop the host, containers and applications share.</summary>
public static class ProtoReadiness
{
    private static readonly HttpClient SharedHttpClient = new();

    /// <summary>Ready when a TCP connection to the endpoint succeeds.</summary>
    public static Func<CancellationToken, ValueTask<bool>> Tcp(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);

        return async cancellationToken =>
        {
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        };
    }

    /// <summary>
    /// Ready when an HTTP GET answers. By default any response - including a 404 - proves the address is
    /// serving; pass <paramref name="ready"/> to demand a health endpoint or a status.
    /// </summary>
    public static Func<CancellationToken, ValueTask<bool>> Http(
        Uri url,
        Func<HttpResponseMessage, bool>? ready = null,
        TimeSpan? requestTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var timeout = requestTimeout ?? TimeSpan.FromSeconds(10);

        return async cancellationToken =>
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation.CancelAfter(timeout);
            try
            {
                using var response = await SharedHttpClient.GetAsync(url, requestCancellation.Token).ConfigureAwait(false);
                return ready?.Invoke(response) ?? true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The request's own timeout, not the caller's cancellation: still not ready.
                return false;
            }
        };
    }

    /// <summary>
    /// Polls a check until it returns <see langword="true"/> or the timeout expires. Exceptions count as
    /// "not ready yet" and are remembered as the last error, so a refused connection while a container
    /// boots is normal; a timeout throws with the name, the attempts and the last error.
    /// </summary>
    public static async ValueTask<ProtoReadinessResult> WaitAsync(
        string name,
        Func<CancellationToken, ValueTask<bool>> check,
        TimeSpan timeout,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(check);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "The readiness timeout must be positive.");
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval), interval, "The readiness interval must be positive.");

        var stopwatch = Stopwatch.StartNew();
        var attempts = 0;
        string? lastError = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                if (await check(cancellationToken).ConfigureAwait(false))
                {
                    return new ProtoReadinessResult(attempts, stopwatch.Elapsed, lastError);
                }

                lastError = null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = $"{exception.GetType().Name}: {exception.Message}";
            }

            if (stopwatch.Elapsed >= timeout)
            {
                var message = $"Readiness probe '{name}' was not satisfied within {timeout.TotalSeconds:0.#}s after {attempts} attempt(s).";
                if (lastError is not null)
                {
                    message += $" Last error: {lastError}";
                }

                throw new InvalidOperationException(message);
            }

            await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
        }
    }
}
