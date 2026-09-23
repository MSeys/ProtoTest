namespace ProtoTest.Web.Tests;

using ProtoTest.Web.Playwright;

[TestFixture]
public sealed class PlaywrightUrlRedactionTests
{
    [Test]
    public void SafeUrl_ShouldRemoveCredentialsQueryAndFragment()
        => Assert.That(
            PlaywrightWebBackend.SafeUrl("https://user:secret@example.test/orders?token=abc#section"),
            Is.EqualTo("https://example.test/orders"));

    [Test]
    public void SafeUrl_WithoutScheme_ShouldReturnInputUnchanged()
        => Assert.That(
            PlaywrightWebBackend.SafeUrl("/orders?token=abc"),
            Is.EqualTo("/orders?token=abc"));

    [Test]
    public void SafeUrl_WithNull_ShouldReturnNull()
        => Assert.That(PlaywrightWebBackend.SafeUrl(null), Is.Null);
}
