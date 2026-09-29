namespace ProtoTest.Core.Tests;

/// <summary>
/// One protocol descriptor owns the coverage fallback: a protocol that ships no category of its own
/// keys under its name, and a declared category wins.
/// </summary>
[TestFixture]
public sealed class ProtoProtocolTests
{
    [Test]
    public void CoverageCategoryOrName_ShouldFallBackToTheProtocolName()
    {
        var withoutCategory = new ProtoProtocol(
            "messaging", "Messaging", "ProtoTest.Messaging", "messaging.receive");
        var withCategory = new ProtoProtocol(
            "rest", "REST", "ProtoTest.Rest", "http.response", "REST");

        Assert.Multiple(() =>
        {
            Assert.That(withoutCategory.CoverageCategory, Is.Null);
            Assert.That(withoutCategory.CoverageCategoryOrName, Is.EqualTo("Messaging"));
            Assert.That(withCategory.CoverageCategoryOrName, Is.EqualTo("REST"));
        });
    }
}
