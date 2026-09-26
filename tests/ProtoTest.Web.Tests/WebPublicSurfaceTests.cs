namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Internal;

/// <summary>
/// Pins the accidental-public cleanup: the web layer's timing defaults and
/// artifact naming are internal plumbing, whichever namespace they were declared in.
/// </summary>
[TestFixture]
public sealed class WebPublicSurfaceTests
{
    [Test]
    public void InternalPlumbing_ShouldNotBePublicApi()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(typeof(WebTiming).IsPublic, Is.False,
                "WebTiming carries the web layer's timing defaults, not a consumer promise");
            Assert.That(typeof(WebNames).IsPublic, Is.False,
                "WebNames carries artifact naming rules both backends share, not a consumer promise");
        }
    }
}
