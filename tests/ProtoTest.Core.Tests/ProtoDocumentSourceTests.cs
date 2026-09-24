namespace ProtoTest.Core.Tests;

using System.Net;

[TestFixture]
public sealed class ProtoDocumentSourceTests
{
    [Test]
    public void LoadText_ShouldReturnInlineContentUnchanged()
        => Assert.Multiple(() =>
        {
            // A JSON body and a multi-line body are inline to Core without help; a format's own
            // prefix is supplied by the integration that understands it.
            Assert.That(ProtoDocumentSource.LoadText("""{"openapi":"3.0.0"}"""),
                Is.EqualTo("""{"openapi":"3.0.0"}"""));
            Assert.That(ProtoDocumentSource.LoadText("line one\nline two"),
                Is.EqualTo("line one\nline two"));
            Assert.That(
                ProtoDocumentSource.LoadText(
                    "type Query { ping: String! }",
                    "https://example.test/graphql",
                    inlinePrefixes: ["type ", "schema "]),
                Is.EqualTo("type Query { ping: String! }"));
        });

    [Test]
    public void LoadText_ShouldResolveRelativeHttpSourceAgainstBaseUrl()
    {
        Uri? requestedUri = null;
        using var client = new HttpClient(new StubHttpHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("schema")
            };
        }));

        var result = ProtoDocumentSource.LoadText("/schema.graphql", "https://example.test/graphql", client);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("schema"));
            Assert.That(requestedUri, Is.EqualTo(new Uri("https://example.test/schema.graphql")));
        });
    }
}
