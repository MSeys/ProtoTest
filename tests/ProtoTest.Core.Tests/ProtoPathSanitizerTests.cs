namespace ProtoTest.Core.Tests;

using NUnit.Framework;
using ProtoTest.Core.Internal;

[TestFixture]
public sealed class ProtoPathSanitizerTests
{
    [TestCase("..")]
    [TestCase(".")]
    [TestCase("...")]
    [TestCase("")]
    public void Segment_ShouldFallBackForEmptyAndDotOnlyValues(string value)
        => Assert.That(ProtoPathSanitizer.Segment(value, "artifact"), Is.EqualTo("artifact"));

    [TestCase("..")]
    [TestCase(".")]
    [TestCase("...")]
    public void FileName_ShouldFallBackForDotOnlyValues(string value)
        => Assert.That(ProtoPathSanitizer.FileName(value, "artifact"), Is.EqualTo("artifact"));

    [Test]
    public void Segment_ShouldKeepOrdinaryValues()
        => Assert.Multiple(() =>
        {
            Assert.That(ProtoPathSanitizer.Segment("orders", "artifact"), Is.EqualTo("orders"));
            Assert.That(ProtoPathSanitizer.Segment("order-42.json", "artifact"), Is.EqualTo("order-42.json"));
        });

    [Test]
    public void FileName_ShouldKeepOrdinaryValues()
        => Assert.That(ProtoPathSanitizer.FileName("response.json", "artifact"), Is.EqualTo("response.json"));

    [Test]
    public void FileName_ShouldSanitizeWindowsInvalidCharactersOnEveryPlatform()
    {
        // On Linux Path.GetInvalidFileNameChars() allows almost all of these; an archive written there
        // must still be readable on Windows, so the segment rule is used instead.
        var sanitized = ProtoPathSanitizer.FileName("a<b>c:d\"e|f?g*h\\i.txt", "artifact");
        Assert.That(sanitized, Is.EqualTo("a_b_c_d_e_f_g_h_i.txt"));
    }
}
