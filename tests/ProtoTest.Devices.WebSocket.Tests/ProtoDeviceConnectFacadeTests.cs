namespace ProtoTest.Devices.WebSocket.Tests;

/// <summary>
/// Pins the compatibility alias: the shared connect bound moved into ProtoTest.Devices, and callers of
/// the old WebSocket location keep working through the forwarding shim.
/// </summary>
[TestFixture]
public sealed class ProtoDeviceConnectFacadeTests
{
    [Test]
    public async Task WithTimeoutAsync_ShouldForwardToTheDevicesCore()
    {
        // Intentional: pins the obsolete shim while it delegates to the new home; CS0618 is expected.
#pragma warning disable CS0618
        var exception = Assert.ThrowsAsync<TimeoutException>(async () =>
            await ProtoDeviceConnect.WithTimeoutAsync(
                "ws://127.0.0.1:1/ws",
                TimeSpan.FromMilliseconds(50),
                async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return "never";
                },
                CancellationToken.None));
#pragma warning restore CS0618

        Assert.That(exception!.Message, Does.Contain("ws://127.0.0.1:1/ws"));
    }
}
