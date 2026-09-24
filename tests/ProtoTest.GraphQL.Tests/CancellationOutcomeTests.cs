namespace ProtoTest.GraphQL.Tests;

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
        // Arrange
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{}}""", Encoding.UTF8, "application/json")
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient(
            "Api",
            "https://example.test/graphql",
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("graphql cancel", "50001", TestMethods.Placeholder);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            // Act
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await Proto.Context.GraphQL("Api")
                    .Request("query { orders }")
                    .ExecuteAsync(cancellation.Token));
        }
        finally
        {
            await host.CompleteTestAsync(ProtoTestResult.Passed);
        }

        // Assert
        var operation = host.Trace.Snapshot().Tests.Single()
            .Entries.Single(entry => entry.Kind == "graphql.operation");
        Assert.That(
            operation.Outcome,
            Is.EqualTo(ProtoTraceOutcome.Cancelled),
            "a cancelled GraphQL request is recorded as cancelled");
    }
}
