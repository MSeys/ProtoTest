namespace ProtoTest.Messaging.Tests;

using ProtoTest.Core;

/// <summary>
/// The cancellation vocabulary: one cancellation rule, applied where the operation fails. A cancelled
/// await records a <c>Cancelled</c> operation.
/// </summary>
[TestFixture]
public sealed class CancellationOutcomeTests
{
    [Test]
    public async Task CancelledAwait_ShouldRecordACancelledOperation()
    {
        // One cancellation rule, applied where the operation fails.
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging cancel", TestMethods.Placeholder);
        var messages = context.Messaging();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await messages.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(2), cancellation.Token));

        // Assert
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single()
            .Entries.Single(entry => entry.Kind == "messaging.await");
        Assert.That(
            operation.Outcome,
            Is.EqualTo(ProtoTraceOutcome.Cancelled),
            "a cancelled await is recorded as cancelled");
    }
}
