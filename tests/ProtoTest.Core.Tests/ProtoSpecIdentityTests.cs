namespace ProtoTest.Core.Tests;

[TestFixture]
public sealed class ProtoSpecIdentityTests
{
    [Test]
    public void Hash_ShouldBeStableLowercaseHex()
    {
        var hash = ProtoSpecIdentity.Hash("openapi: 3.0.1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hash, Has.Length.EqualTo(64), "SHA-256 hex.");
            Assert.That(hash, Is.EqualTo(hash.ToLowerInvariant()));
            Assert.That(hash, Is.EqualTo(ProtoSpecIdentity.Hash("openapi: 3.0.1")));
            Assert.That(ProtoSpecIdentity.Hash("openapi: 3.0.2"), Is.Not.EqualTo(hash));
        }
    }

    [Test]
    public void Metadata_ShouldCarryTheSourceAndTheHash()
    {
        var metadata = ProtoSpecIdentity.Metadata("openapi.json", "content");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metadata[ProtoSpecIdentity.SourceMetadataKey], Is.EqualTo("openapi.json"));
            Assert.That(metadata[ProtoSpecIdentity.HashMetadataKey], Is.EqualTo(ProtoSpecIdentity.Hash("content")));
        }
    }

    [Test]
    public void Metadata_ShouldRedactCredentialsAndSensitiveQueryValuesInAUrlSource()
    {
        var metadata = ProtoSpecIdentity.Metadata(
            "https://user:secret@example.test/openapi.json?token=abc&v=2", "content");

        Assert.That(metadata[ProtoSpecIdentity.SourceMetadataKey],
            Is.EqualTo("https://example.test/openapi.json?token=%5BREDACTED%5D&v=2"));
    }

    [Test]
    public void Metadata_ShouldRecordAnInlineSourceVerbatim()
    {
        const string inline = """
            openapi: 3.0.1
            servers:
              - url: https://api.example.test/v1
            """;

        var metadata = ProtoSpecIdentity.Metadata(inline, inline);

        Assert.That(metadata[ProtoSpecIdentity.SourceMetadataKey], Is.EqualTo(inline),
            "An inline document is recorded as it is; only an absolute URL is sanitized.");
    }
}
