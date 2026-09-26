namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Playwright;

/// <summary>
/// Playwright operation correlation keeps the parent lineage while an outer
/// operation is still open, and the trace file reader applies the cap and the teardown token.
/// </summary>
[TestFixture]
public sealed class PlaywrightEvidenceTests
{
    [Test]
    public void Correlation_ShouldFallBackToTheMostRecentlyBegunOpenOperation()
    {
        var state = new PlaywrightCorrelationState();

        state.Open("outer");
        state.Open("inner");
        state.Close("inner");

        Assert.Multiple(() =>
        {
            Assert.That(state.Latest, Is.EqualTo("outer"),
                "closing the nested operation must not forget the open parent");
            Assert.That(state.IsOpen("outer"), Is.True);
        });

        state.Close("outer");
        Assert.That(state.Latest, Is.Null, "the last open operation leaves no correlation behind");
    }

    [Test]
    public void Correlation_ShouldTrackBeginOrderWhenASiblingStaysOpen()
    {
        var state = new PlaywrightCorrelationState();

        state.Open("first");
        state.Open("second");
        state.Close("second");

        Assert.That(state.Latest, Is.EqualTo("first"), "the latest remaining operation is the fallback");
    }

    [Test]
    public async Task TraceFile_ShouldApplyTheCapAndObserveTheToken()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-playwright-trace-test-{Guid.NewGuid():N}.zip");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[2048]);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    await PlaywrightTraceFile.ReadAsync(path, 1024, CancellationToken.None),
                    Is.Null,
                    "a trace over the cap is not buffered; the caller records the cap event");
                Assert.That(
                    await PlaywrightTraceFile.ReadAsync(path, 4096, CancellationToken.None),
                    Has.Length.EqualTo(2048));
                Assert.That(
                    await PlaywrightTraceFile.ReadAsync(path, 0, CancellationToken.None),
                    Has.Length.EqualTo(2048),
                    "zero disables the cap");
            }

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await PlaywrightTraceFile.ReadAsync(path, 4096, cancelled.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
