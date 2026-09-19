namespace ProtoTest.Http.Tests;

using ProtoTest.Http;

[TestFixture]
public sealed class ProtoHttpDiagnosticSanitizerTests
{
    [Test]
    public void SanitizeUri_ShouldStripUserInfoAndRedactSensitiveQueryValues()
    {
        var options = new ProtoHttpAttachmentOptions();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ProtoHttpDiagnosticSanitizer.SanitizeUri(
                    new Uri("https://user:secret@example.test/callback?code=abc&access_token=xyz#done"),
                    options),
                Is.EqualTo("https://example.test/callback?code=abc&access_token=%5BREDACTED%5D#done"));

            Assert.That(
                ProtoHttpDiagnosticSanitizer.SanitizeUri(
                    new Uri("https://user:secret@example.test/orders"),
                    options),
                Is.EqualTo("https://example.test/orders"));

            Assert.That(ProtoHttpDiagnosticSanitizer.SanitizeUri(null, options), Is.Null);
        }
    }

    [Test]
    public void SanitizeUri_ShouldKeepQueryValuesWhenRedactionIsDisabled()
    {
        var options = new ProtoHttpAttachmentOptions { RedactSensitiveData = false };

        Assert.That(
            ProtoHttpDiagnosticSanitizer.SanitizeUri(
                new Uri("https://user:secret@example.test/callback?access_token=xyz"),
                options),
            Is.EqualTo("https://example.test/callback?access_token=xyz"));
    }

    [Test]
    public void SanitizeHeaders_ShouldRedactSensitiveHeadersAndKeepOthers()
    {
        var options = new ProtoHttpAttachmentOptions();
        var headers = new[]
        {
            new KeyValuePair<string, IEnumerable<string>>("Authorization", ["Bearer secret"]),
            new KeyValuePair<string, IEnumerable<string>>("X-Api-Key", ["key-1", "key-2"]),
            new KeyValuePair<string, IEnumerable<string>>("Accept", ["application/json", "text/plain"])
        };

        var sanitized = ProtoHttpDiagnosticSanitizer.SanitizeHeaders(headers, options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sanitized["Authorization"], Is.EqualTo("[REDACTED]"));
            Assert.That(sanitized["X-Api-Key"], Is.EqualTo("[REDACTED]"));
            Assert.That(sanitized["Accept"], Is.EqualTo("application/json, text/plain"));
        }
    }

    [Test]
    public void SanitizeHeaders_ShouldKeepValuesWhenRedactionIsDisabled()
    {
        var options = new ProtoHttpAttachmentOptions { RedactSensitiveData = false };

        var sanitized = ProtoHttpDiagnosticSanitizer.SanitizeHeaders(
            [new KeyValuePair<string, IEnumerable<string>>("Authorization", ["Bearer secret"])],
            options);

        Assert.That(sanitized["Authorization"], Is.EqualTo("Bearer secret"));
    }

    [Test]
    public void SanitizeHeaders_ShouldRespectCustomSensitiveHeaders()
    {
        var options = new ProtoHttpAttachmentOptions();
        options.SensitiveHeaders.Add("X-Tenant-Secret");

        var sanitized = ProtoHttpDiagnosticSanitizer.SanitizeHeaders(
            [
                new KeyValuePair<string, IEnumerable<string>>("X-Tenant-Secret", ["value"]),
                new KeyValuePair<string, IEnumerable<string>>("Authorization", ["Bearer secret"])
            ],
            options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sanitized["X-Tenant-Secret"], Is.EqualTo("[REDACTED]"));
            Assert.That(sanitized["Authorization"], Is.EqualTo("[REDACTED]"));
        }
    }
}
