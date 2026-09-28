namespace ProtoTest.Feedback.Tests;

/// <summary>
/// The github-annotations channel: the exact workflow command lines for a digest, one per failing test
/// and per failed gate, with the runner's escaping.
/// </summary>
[TestFixture]
public sealed class FeedbackAnnotationsTests
{
    [Test]
    public void Lines_ShouldRenderOneErrorPerFailingTestWithItsSourceLocation()
    {
        var lines = ProtoFeedbackAnnotations.Lines(FeedbackFixtures.FailedDigest());

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(
            lines[0],
            Is.EqualTo("::error file=tests/Orders/OrderTests.cs,line=42::orders match their shape: Shape mismatch failed with 1 error(s)."));
    }

    [Test]
    public void Lines_ShouldEscapeTheMessageAndTheProperties()
    {
        var digest = FeedbackFixtures.FailedDigest(
            errorMessage: "line one\r\nline 50%: done",
            sourceFile: @"C:\reports\orders,shape.cs",
            sourceLine: 7);

        var line = ProtoFeedbackAnnotations.Lines(digest).Single();

        Assert.That(
            line,
            Is.EqualTo(@"::error file=C%3A\reports\orders%2Cshape.cs,line=7::orders match their shape: line one%0D%0Aline 50%25: done"));
    }

    [Test]
    public void Lines_ShouldOmitTheLocationWithoutOne()
    {
        var digest = FeedbackFixtures.FailedDigest(sourceFile: null, sourceLine: null);

        Assert.That(
            ProtoFeedbackAnnotations.Lines(digest).Single(),
            Is.EqualTo("::error::orders match their shape: Shape mismatch failed with 1 error(s)."));
    }

    [Test]
    public void Lines_ShouldAnnotateAFailedGate()
    {
        var lines = ProtoFeedbackAnnotations.Lines(FeedbackFixtures.FailedDigest(failedGate: true));

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[1], Is.EqualTo("::error::Run gate 'coverage gate' failed: Coverage regressed below the agreed floor."));
    }

    [Test]
    public void Lines_ShouldNotAnnotateASkippedTest()
    {
        Assert.That(ProtoFeedbackAnnotations.Lines(FeedbackFixtures.FailedDigest(outcome: "skipped")), Is.Empty);
    }

    [Test]
    public void Write_ShouldReportTheCountOrTheReasonItSkipped()
    {
        using var writer = new StringWriter();

        var posted = ProtoFeedbackAnnotations.Write(FeedbackFixtures.FailedDigest(), writer);
        var skipped = ProtoFeedbackAnnotations.Write(FeedbackFixtures.GreenDigest(), writer);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(posted.Channel, Is.EqualTo(ProtoFeedbackChannels.GithubAnnotations));
            Assert.That(posted.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(posted.Reason, Is.EqualTo("1 annotation."));
            Assert.That(writer.ToString(), Does.Contain("::error file=tests/Orders/OrderTests.cs,line=42::"));
            Assert.That(skipped.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(skipped.Reason, Does.Contain("no failures to annotate"));
        }
    }

    [Test]
    public void Write_ShouldKeepAMultiLineMessageOnOneCommandLine()
    {
        using var writer = new StringWriter();
        ProtoFeedbackAnnotations.Write(
            FeedbackFixtures.FailedDigest(errorMessage: "first line\r\nsecond line"), writer);

        var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(1));
    }
}
