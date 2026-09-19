namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Internal;

[TestFixture]
public sealed class WebPagePathTests
{
    [Test]
    public void Visit_ShouldMatchInventoryRouteWithASpace()
    {
        var visit = WebPagePath.FromAddress("https://example.test/a%20b");
        var inventory = WebPagePath.Normalize("/a b");

        Assert.Multiple(() =>
        {
            Assert.That(visit, Is.EqualTo("/a b"));
            Assert.That(inventory, Is.EqualTo("/a b"));
            Assert.That(WebPagePath.Matches(inventory, visit), Is.True,
                "the escaped visit and the decoded inventory route are one identity");
        });
    }

    [Test]
    public void Visit_ShouldMatchInventoryRouteWithNonAsciiCharacters()
    {
        var visit = WebPagePath.FromAddress("https://example.test/caf%C3%A9");
        var inventory = WebPagePath.Normalize("/café");

        Assert.Multiple(() =>
        {
            Assert.That(visit, Is.EqualTo("/café"));
            Assert.That(inventory, Is.EqualTo("/café"));
            Assert.That(WebPagePath.Matches(inventory, visit), Is.True);
        });
    }

    [Test]
    public void EncodedSlash_ShouldStayOneSegment()
    {
        var visit = WebPagePath.FromAddress("https://example.test/files/a%2Fb");
        var inventory = WebPagePath.Normalize("/files/a%2Fb");

        Assert.Multiple(() =>
        {
            Assert.That(visit, Is.EqualTo("/files/a%2Fb"), "the encoded slash is not a separator");
            Assert.That(inventory, Is.EqualTo("/files/a%2Fb"));
            Assert.That(WebPagePath.Matches("/files/{name}", visit), Is.True,
                "{name} matches the single segment");
            Assert.That(WebPagePath.Matches("/files/{name}/edit", visit), Is.False,
                "the encoded slash does not create the /edit segment");
            Assert.That(WebPagePath.Normalize("/files/a/b"), Is.Not.EqualTo(visit),
                "a real separator stays a separator");
        });
    }

    [Test]
    public void Matches_ShouldRequireARestPatternToBeLast()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebPagePath.Matches("/files/{...}", "/files/a/b"), Is.True);
            Assert.That(WebPagePath.Matches("/files/{...}", "/files"), Is.True,
                "a rest pattern matches the rest including none");
            Assert.That(WebPagePath.Matches("/files/{...}/edit", "/files/a/edit"), Is.False,
                "a literal segment after a rest pattern can never be satisfied");
            Assert.That(WebPagePath.Matches("/files/{...}/edit", "/files/a/b/edit"), Is.False);
        });
    }

    [Test]
    public void Normalize_ShouldStripTheQueryBeforeClassifyingAnAbsoluteUrlInIt()
    {
        var normalized = WebPagePath.Normalize("/checkout?next=https://example.test/pay");

        Assert.That(normalized, Is.EqualTo("/checkout"));
    }

    [Test]
    public void NormalizeRoute_ShouldMapDynamicSegments()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebPagePath.NormalizeRoute("/users/:id"), Is.EqualTo("/users/{id}"));
            Assert.That(WebPagePath.NormalizeRoute("/users/:id?"), Is.EqualTo("/users/{id}"));
            Assert.That(WebPagePath.NormalizeRoute("/legacy/*"), Is.EqualTo("/legacy/{...}"));
            Assert.That(WebPagePath.NormalizeRoute("/docs/[...slug]"), Is.EqualTo("/docs/{...}"));
            Assert.That(WebPagePath.NormalizeRoute("/docs/[[...slug]]"), Is.EqualTo("/docs/{...}"));
            Assert.That(WebPagePath.NormalizeRoute("/files/$"), Is.EqualTo("/files/{...}"));
            Assert.That(WebPagePath.NormalizeRoute("/files/$id"), Is.EqualTo("/files/{id}"));
        });
    }
}
