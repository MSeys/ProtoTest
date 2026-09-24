namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.TestSupport;

/// <summary>
/// Stage 0 characterization for the cancellation vocabulary (Audit 3, finding D3). A cancelled request
/// records a <c>Failed</c> operation today; Web and Data record <c>Cancelled</c>. Stage 3 applies one
/// rule and flips this assertion.
/// </summary>
[TestFixture]
public sealed class CancellationOutcomeTests
{
    [Test]
    public async Task CancelledRequest_ShouldRecordACancelledOperation()
    {
        // Stage 3 (Audit 3, finding D3): one cancellation rule, applied where the operation fails.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient(
            "Orders",
            "https://orders.test",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("Cancellation", "20021", TestMethods.Placeholder);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            // Act
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await Proto.Context.Rest("Orders").GetAsync("/orders/1", ct: cancellation.Token));
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }

        // Assert
        var operation = host.Trace.Snapshot().Tests.Single()
            .Entries.Single(entry => entry.Kind == "http.request");
        Assert.That(
            operation.Outcome,
            Is.EqualTo(ProtoTraceOutcome.Cancelled),
            "a cancelled request is recorded as cancelled");
    }
}
