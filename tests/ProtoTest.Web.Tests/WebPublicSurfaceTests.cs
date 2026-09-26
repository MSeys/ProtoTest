namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Internal;

/// <summary>
/// Pins the accidental-public cleanup of Audit 5 A5-53 (C-10): the web layer's timing defaults and
/// artifact naming are internal plumbing, whichever namespace they were declared in.
/// </summary>
[TestFixture]
public sealed class WebPublicSurfaceTests
{
    [Test]
    public void InternalPlumbing_ShouldNotBePublicApi()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(WebTiming).IsPublic, Is.False,
                "WebTiming carries the web layer's timing defaults, not a consumer promise");
            Assert.That(typeof(WebNames).IsPublic, Is.False,
                "WebNames carries artifact naming rules both backends share, not a consumer promise");
        });
    }
}
