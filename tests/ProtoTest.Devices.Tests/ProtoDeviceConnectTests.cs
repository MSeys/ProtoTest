namespace ProtoTest.Devices.Tests;

/// <summary>
/// Pins the shared connect bound every device transport uses: the attempt token is the timeout
/// authority, a failure that surfaces after it is reported as a <see cref="TimeoutException"/> naming
/// the target, and a caller's own cancellation is never reclassified.
/// </summary>
[TestFixture]
public sealed class ProtoDeviceConnectTests
{
    [Test]
    public async Task WithTimeoutAsync_ShouldReturnTheConnectionTheCallProduces()
    {
        var connection = await ProtoDeviceConnect.WithTimeoutAsync(
            "ws://127.0.0.1:1/ws",
            TimeSpan.FromSeconds(5),
            _ => Task.FromResult("connected"),
            CancellationToken.None);

        Assert.That(connection, Is.EqualTo("connected"));
    }

    [Test]
    public async Task WithTimeoutAsync_WhenTheAttemptExpires_ShouldThrowTimeoutExceptionNamingTheTarget()
    {
        var exception = Assert.ThrowsAsync<TimeoutException>(async () =>
            await ProtoDeviceConnect.WithTimeoutAsync(
                "ws://127.0.0.1:1/ws",
                TimeSpan.FromMilliseconds(100),
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return "never";
                },
                CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("ws://127.0.0.1:1/ws"));
            Assert.That(exception.Message, Does.Contain("timed out after 0.1s"));
        }
    }

    [Test]
    public async Task WithTimeoutAsync_WhenATransportAbortsAnotherWay_ShouldStillReportTheTimeout()
    {
        // TestServer can surface an aborted handshake as an incomplete-handshake response instead of an
        // OperationCanceledException; the attempt token still decides.
        var exception = Assert.ThrowsAsync<TimeoutException>(async () =>
            await ProtoDeviceConnect.WithTimeoutAsync(
                "in-process:/ws",
                TimeSpan.FromMilliseconds(50),
                token =>
                {
                    token.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
                    return Task.FromException<string>(new InvalidOperationException("incomplete handshake"));
                },
                CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("in-process:/ws"));
    }

    [Test]
    public async Task WithTimeoutAsync_WhenTheCallerCancels_ShouldPropagateTheCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        var exception = Assert.CatchAsync<OperationCanceledException>(async () =>
            await ProtoDeviceConnect.WithTimeoutAsync(
                "ws://127.0.0.1:1/ws",
                TimeSpan.FromSeconds(30),
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return "never";
                },
                cancellation.Token));

        Assert.That(exception, Is.Not.InstanceOf<TimeoutException>(), "the caller's cancellation wins over the timeout");
    }
}
