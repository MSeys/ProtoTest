namespace ProtoTest.OpenApi.Tests;

using NUnit.Framework;
using ProtoTest.OpenApi.Internal;
using System.Net;

[TestFixture]
public class OpenApiSpecLoaderTests
{
    [Test]
    public void Load_ShouldParseValidJsonContent()
    {
        // Act
        var doc = OpenApiSpecLoader.Load(OpenApiTestHelper.SampleJsonSpec);

        // Assert
        Assert.That(doc, Is.Not.Null);
        Assert.That(doc.Paths.ContainsKey("/users/{id}"), Is.True);
    }

    [Test]
    public void Load_ShouldThrow_WhenContentIsInvalid()
    {
        // Arrange
        var invalidJson = "{ \"openapi\": \"3.0.0\" }";

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => OpenApiSpecLoader.Load(invalidJson));
        Assert.That(ex.Message, Does.Contain("Failed to parse OpenAPI specification"));
    }

    [Test]
    public void Load_ShouldRetrieveRelativeUrlUsingBaseUrl()
    {
        // Arrange
        using var client = new HttpClient(new OpenApiDocumentHandler());

        // Act
        var document = OpenApiSpecLoader.Load(
            "/swagger/v1.json",
            "https://example.test/api/",
            client);

        // Assert
        Assert.That(document.Paths.ContainsKey("/users/{id}"), Is.True);
    }

    private sealed class OpenApiDocumentHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.That(request.RequestUri?.AbsoluteUri, Is.EqualTo("https://example.test/swagger/v1.json"));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OpenApiTestHelper.SampleJsonSpec)
            });
        }
    }
}
