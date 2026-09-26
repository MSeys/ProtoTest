namespace ProtoTest.Http.Tests;

using ProtoTest.Core;
using ProtoTest.Json;

/// <summary>
/// Stage 5 (Audit 3, finding D6): a diagnostic capture failure is traced evidence instead of being
/// swallowed silently, matching the gRPC, messaging and web diagnostic paths. Audit 5 A5.9 (B12): the
/// guard takes the protocol's identity, so the event names the protocol that failed, not HTTP.
/// </summary>
[TestFixture]
public sealed class ProtoObservationCaptureTests
{
    [Test]
    public async Task ThrowingObservationFactory_ShouldBeTracedWithTheProtocolIdentityAndNotThrow()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("diagnostics", "00063", TestMethods.Placeholder);
        var protocol = new ProtoProtocol("Fake", "Fake protocol", "ProtoTest.Fake", "fake.response");

        // Act
        Assert.DoesNotThrow(() => ProtoObservationCapture.TryRecord(
            context,
            protocol,
            () => throw new InvalidOperationException("The diagnostic failed.")));

        // Assert
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        var entry = test.Entries.Single(item => item.Kind == "fake.diagnostics.failed");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Source, Is.EqualTo("ProtoTest.Fake"));
            Assert.That(entry.Name, Is.EqualTo("Fake protocol diagnostic capture failed"));
            Assert.That(entry.Error!.Message, Is.EqualTo("The diagnostic failed."));
        });
        await host.StopAsync();
    }
}
