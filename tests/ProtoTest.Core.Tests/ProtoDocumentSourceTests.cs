namespace ProtoTest.Core.Tests;

using System.Net;

[TestFixture]
public sealed class ProtoDocumentSourceTests
{
    [Test]
    public void LoadText_ShouldReturnInlineContentUnchanged()
        => Assert.That(ProtoDocumentSource.LoadText("type Query { ping: String! }", "https://example.test/graphql"),
            Is.EqualTo("type Query { ping: String! }"));

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
