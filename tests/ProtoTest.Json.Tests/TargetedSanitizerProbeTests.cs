namespace ProtoTest.Json.Tests;

[TestFixture]
public sealed class TargetedSanitizerProbeTests
{
    [Test]
    public void MultipartValueLineStartingWithDashes_ShouldStillBeRedacted()
    {
        var body = "--b\r\nContent-Disposition: form-data; name=\"password\"\r\n\r\n" +
            "secret-one\r\n--not-a-boundary\r\nsecret-two\r\n--b--";

        var result = JsonDiagnosticSanitizer.Sanitize(body);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Not.Contain("secret-one"));
            Assert.That(result, Does.Not.Contain("secret-two"));
        });
    }

    [Test]
    public void MultipartSanitizer_ShouldNotBlowUpOnHostileUnterminatedHeaders()
    {
        // A hostile body made only of unmatched Content-Disposition quotes on one line.
        var hostile = string.Concat(Enumerable.Repeat("Content-Disposition:name=\"x", 4000));
        var work = Task.Run(() => JsonDiagnosticSanitizer.Sanitize(hostile));

        var completed = work.Wait(TimeSpan.FromSeconds(5));
        Assert.That(completed, Is.True,
            "the sanitizer must not backtrack exponentially on hostile bodies");
        Assert.That(work.Result, Is.Not.Null);
    }

    [Test]
    public void XmlSanitizer_ShouldNotBlowUpOnHostileUnterminatedTags()
    {
        var hostile = "<a " + string.Concat(Enumerable.Repeat("\"", 200000));
        var work = Task.Run(() => JsonDiagnosticSanitizer.Sanitize(hostile));

        var completed = work.Wait(TimeSpan.FromSeconds(5));
        Assert.That(completed, Is.True);
        Assert.That(work.Result, Is.Not.Null);
    }
}
