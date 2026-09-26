namespace ProtoTest.Http.Tests;

using ProtoTest.Json;

[TestFixture]
public sealed class ProtoHttpMechanicsTests
{
    [Test]
    public void Headers_ShouldFallBackToTheContentAndThrowWhenNeitherAcceptsTheHeader()
    {
        using var withContent = new HttpRequestMessage(HttpMethod.Post, "https://example.test/orders")
        {
            Content = new StringContent("{}")
        };
        using var withoutContent = new HttpRequestMessage(HttpMethod.Get, "https://example.test/orders");
        var headers = new Dictionary<string, string>
        {
            ["X-Correlation"] = "abc",
            ["Content-Type"] = "application/json"
        };

        ProtoHttpHeaders.Apply(withContent, headers);

        Assert.Multiple(() =>
        {
            Assert.That(withContent.Headers.GetValues("X-Correlation"), Is.EqualTo(new[] { "abc" }));
            Assert.That(
                () => ProtoHttpHeaders.Apply(withoutContent, headers),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("Content-Type"));
        });
    }

    [Test]
    public void FailureDiagnostics_ShouldSanitizeTheAddressAndMessage()
    {
        var diagnostics = ProtoFailureDiagnostics.From(
            new Uri("https://user:secret@example.test/orders?access_token=xyz"),
            new InvalidOperationException("""{"token":"hunter2","note":"failed"}"""),
            CancellationToken.None,
            new ProtoHttpAttachmentOptions(),
            new ProtoHttpAttachmentOptions().SensitiveQueryParameters);

        Assert.Multiple(() =>
        {
            Assert.That(diagnostics.RequestUri, Is.EqualTo("https://example.test/orders?access_token=%5BREDACTED%5D"));
            Assert.That(diagnostics.Message, Does.Contain("[REDACTED]"));
            Assert.That(diagnostics.Message, Does.Not.Contain("hunter2"));
            Assert.That(diagnostics.ExceptionType, Is.EqualTo(typeof(InvalidOperationException).FullName));
            Assert.That(diagnostics.IsCanceled, Is.False);
        });
    }

    [Test]
    public void FailureDiagnostics_ShouldRecognizeCancellationFromTheTokenOrTheException()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Multiple(() =>
        {
            Assert.That(
                ProtoFailureDiagnostics.From(null, new InvalidOperationException(), cancellation.Token, null).IsCanceled,
                Is.True);
            Assert.That(
                ProtoFailureDiagnostics.From(
                    null,
                    new OperationCanceledException(),
                    CancellationToken.None,
                    null).IsCanceled,
                Is.True);
        });
    }

    [Test]
    public async Task Endpoint_ShouldResolveFromTheClientAndValidateTheScheme()
    {
        using var client = new HttpClient { BaseAddress = new Uri("https://example.test/graphql") };

        var resolved = await ProtoHttpEndpoint.ResolveBaseAddressAsync(
            client,
            "GraphQL",
            "Catalog",
            resolver: null,
            context: null!,
            CancellationToken.None);

        Assert.That(resolved, Is.EqualTo(new Uri("https://example.test/graphql")));
        Assert.That(
            async () => await ProtoHttpEndpoint.ResolveBaseAddressAsync(
                client,
                "GraphQL",
                "Catalog",
                (_, _) => ValueTask.FromResult(new Uri("file:///tmp/graphql")),
                context: null!,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains("absolute HTTP or HTTPS URI"));
    }

    [Test]
    public void Endpoint_WithoutAClientAddress_ShouldNameTheClientInTheError()
    {
        using var client = new HttpClient();

        Assert.That(
            async () => await ProtoHttpEndpoint.ResolveBaseAddressAsync(
                client,
                "GraphQL",
                "Catalog",
                resolver: null,
                context: null!,
                CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("GraphQL client 'Catalog' has no endpoint."));
    }
}
