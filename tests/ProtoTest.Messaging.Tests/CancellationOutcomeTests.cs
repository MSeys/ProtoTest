namespace ProtoTest.Messaging.Tests;

using ProtoTest.Core;

/// <summary>
/// Stage 0 characterization for the cancellation vocabulary (Audit 3, finding D3). A cancelled await
/// records a <c>Failed</c> operation today; Web and Data record <c>Cancelled</c>. Stage 3 applies one
/// rule and flips this assertion.
/// </summary>
[TestFixture]
public sealed class CancellationOutcomeTests
{
    [Test]
    public async Task CancelledAwait_ShouldRecordACancelledOperation()
    {
        // Stage 3 (Audit 3, finding D3): one cancellation rule, applied where the operation fails.
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
