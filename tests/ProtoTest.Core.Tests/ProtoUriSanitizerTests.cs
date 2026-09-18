namespace ProtoTest.Core.Tests;

using NUnit.Framework;

[TestFixture]
public sealed class ProtoUriSanitizerTests
{
    [Test]
    public void WithoutUserInfo_ShouldRemoveCredentialsAndKeepTheRest()
        => Assert.That(
            ProtoUriSanitizer.WithoutUserInfo("https://user:secret@example.test:8443/api?x=1#frag"),
            Is.EqualTo("https://example.test:8443/api?x=1#frag"));

    [Test]
    public void WithoutUserInfo_ShouldRemoveUserNameWithoutPassword()
        => Assert.That(
            ProtoUriSanitizer.WithoutUserInfo("amqp://guest@example.test/"),
            Is.EqualTo("amqp://example.test/"));

    [Test]
    public void WithoutUserInfo_ShouldHandleAnAtSignInThePath()
        => Assert.That(
            ProtoUriSanitizer.WithoutUserInfo("https://example.test/users/@me?x=1"),
            Is.EqualTo("https://example.test/users/@me?x=1"));

    [Test]
    public void Sanitize_ShouldRedactSensitiveQueryValuesAndKeepOthers()
        => Assert.That(
            ProtoUriSanitizer.Sanitize(
                new Uri("https://user:secret@example.test/callback?code=abc&access_token=xyz#done"),
                ProtoUriSanitizer.DefaultSensitiveQueryParameters),
            Is.EqualTo("https://example.test/callback?code=abc&access_token=%5BREDACTED%5D#done"));

    [Test]
    public void Sanitize_ShouldStripCredentialsEvenWithoutRedactionRules()
        => Assert.That(
            ProtoUriSanitizer.Sanitize(new Uri("http://user:secret@example.test/"), null),
            Is.EqualTo("http://example.test/"));

    [Test]
    public void Sanitize_ShouldReturnNullForANullAddress()
        => Assert.That(ProtoUriSanitizer.Sanitize((Uri?)null), Is.Null);

    [Test]
    public void Sanitize_ShouldLeaveAnOpaqueAddressUnchanged()
        => Assert.That(
            ProtoUriSanitizer.Sanitize("mailto:someone@example.test", ProtoUriSanitizer.DefaultSensitiveQueryParameters),
            Is.EqualTo("mailto:someone@example.test"));
}
