namespace ProtoTest.Http.Tests;

using ProtoTest.Core;

/// <summary>
/// Stage 5 (Audit 3, finding D6): an HTTP diagnostic capture failure is traced evidence instead of being
/// swallowed silently, matching the gRPC, messaging and web diagnostic paths.
/// </summary>
[TestFixture]
public sealed class ProtoObservationCaptureTests
{
    [Test]
    public async Task ThrowingObservationFactory_ShouldBeTracedAndNotThrow()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("diagnostics", "00063", TestMethods.Placeholder);

        // Act
        Assert.DoesNotThrow(() => ProtoObservationCapture.TryRecord(
            context,
            () => throw new InvalidOperationException("The diagnostic failed.")));

        // Assert
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.That(test.Entries.Any(entry => entry.Kind == "http.diagnostics.failed"), Is.True);
        await host.StopAsync();
    }
}
