namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;

[TestFixture]
public class RestRequestBuilderTests
{
    private TestHttpMessageHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private ProtoExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://api.prototest.dev") };

        var services = new ServiceCollection();
        services.AddTransient<TestDummyAuthenticator>();
        services.AddKeyedSingleton(ProtoRestBuilder.ProtocolName, new ProtoHttpAttachmentOptions());
        _context = new ProtoExecutionContext("", services.BuildServiceProvider().CreateScope(), "00000", (MethodInfo)MethodInfo.GetCurrentMethod()!);
    }

    [TearDown]
    public async Task TearDown()
    {
        _handler.Dispose();
        _httpClient.Dispose();
        await _context.DisposeAsync();
    }

    [Test]
    public async Task SendAsync_Should_Build_Correct_Request_Headers_And_Route()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"status":"ok"}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Header("X-Custom-Header", "TestValue")
            .Header("User-Agent", "ProtoTest-Runner");

        // Act
        var response = await builder.GetAsync("/users/{id}", new { id = 42, active = true });

        // Assert - Request Verification
        Assert.That(_handler.LastRequest, Is.Not.Null);
        Assert.That(_handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(_handler.LastRequest.RequestUri?.ToString(), Is.EqualTo("https://api.prototest.dev/users/42?active=true"));
        Assert.That(_handler.LastRequest.Headers.GetValues("X-Custom-Header").Single(), Is.EqualTo("TestValue"));
        Assert.That(_handler.LastRequest.Headers.GetValues("User-Agent").Single(), Is.EqualTo("ProtoTest-Runner"));

        // Assert - Response Verification via RestResponse Helpers
        response.Should.HaveHttpStatus(HttpStatusCode.OK);

        var body = response.ReadAsAnonymous(new { status = "" });
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.status, Is.EqualTo("ok"));
    }

    [Test]
    public async Task SendAsync_ShouldEmitRestResponseObservation()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id": 1}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        // Act
        await builder.GetAsync("/users/{id}", new { id = 1 });

        // Assert - Context Recorded Hits
        var hit = _context.RecordedObservations.FirstOrDefault(h => h.Data is RestResponseData);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.TargetName, Is.EqualTo("TestTarget"));
        Assert.That(hit.Identifier, Is.EqualTo("GET /users/{id}"));

        var data = (RestResponseData)hit.Data!;
        Assert.That(data.Method, Is.EqualTo("GET"));
        Assert.That(data.RouteTemplate, Is.EqualTo("/users/{id}"));
        Assert.That(data.StatusCode, Is.EqualTo(200));
        Assert.That(data.ResponseBody, Is.EqualTo("""{"id": 1}"""));
    }

    [Test]
    public async Task SendAsync_ShouldPreserveBinaryResponseAttachment()
    {
        var expected = new byte[] { 0, 1, 2, 128, 255 };
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(expected)
        };
        _handler.ResponseToReturn.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var response = await new RestRequestBuilder(_httpClient, _context, "Files").GetAsync("/report.xlsx");

        Assert.That(_context.Attachments, Has.Count.EqualTo(1));
        Assert.That(_context.Attachments[0].MediaType,
            Is.EqualTo("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        Assert.That(await _context.Attachments[0].ReadAllBytesAsync(), Is.EqualTo(expected));
    }

    [Test]
    public async Task SendAsync_ShouldKeepRawResponseAliveUntilRestResponseIsDisposed()
    {
        var content = new TrackingContent("raw response");
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        var response = await builder.GetAsync("/raw");

        Assert.That(content.IsDisposed, Is.True, "The network content is replaced by a bounded in-memory copy.");
        Assert.That(await response.RawResponse.Content.ReadAsStringAsync(), Is.EqualTo("raw response"));

        response.Dispose();
        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await response.RawResponse.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task Body_Object_ShouldSerializeJsonAndEmitShapeMatchObservation()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id": 1, "created": true}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Body(new { name = "Matthias", role = "Admin" });

        // Act
        var response = await builder.PostAsync("/users");

        // Assert - Request Verification
        Assert.That(_handler.LastRequest?.Content, Is.Not.Null);
        Assert.That(_handler.LastRequest!.Content!.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));

        Assert.That(_handler.LastRequestBody, Is.Not.Null);
        Assert.That(_handler.LastRequestBody, Contains.Substring("Matthias"));

        // Assert - Response Verification & Shape Hit Recording
        response
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { id = JsonValue.GreaterThan(0), created = true });

        var shapeHit = _context.RecordedObservations.FirstOrDefault(h => h.Data is RestShapeMatchData);
        Assert.That(shapeHit, Is.Not.Null);

        var shapeData = (RestShapeMatchData)shapeHit!.Data!;
        Assert.That(shapeData.MatchedProperties, Contains.Item("$.id"));
        Assert.That(shapeData.MatchedProperties, Contains.Item("$.created"));
        Assert.That(_context.Attachments.Select(attachment => attachment.Name), Is.EqualTo(new[]
        {
            "00000-rest-01-request",
            "00000-rest-01-response",
            "00000-rest-01-expected-shape"
        }));
        Assert.That(_context.Attachments[1].MediaType, Is.EqualTo("text/plain"));
        Assert.That(
            System.Text.Encoding.UTF8.GetString(await _context.Attachments[1].ReadAllBytesAsync()),
            Is.EqualTo("""{"id": 1, "created": true}"""));
        Assert.That(
            System.Text.Encoding.UTF8.GetString(await _context.Attachments[2].ReadAllBytesAsync()),
            Does.Contain("constraint: greater than 0"));
    }

    [Test]
    public async Task Body_RawString_Should_Set_Custom_MediaType()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<response>ok</response>", System.Text.Encoding.UTF8, "application/xml")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Body("<xml><user>Matthias</user></xml>", "application/xml");

        // Act
        var response = await builder.PostAsync("/xml-endpoint");

        // Assert - Request & Response Verification
        Assert.That(_handler.LastRequest!.Content!.Headers.ContentType?.MediaType, Is.EqualTo("application/xml"));
        Assert.That(_handler.LastRequestBody, Is.EqualTo("<xml><user>Matthias</user></xml>"));

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(response.Content, Is.EqualTo("<response>ok</response>"));
    }

    [Test]
    public async Task Auth_Instance_Should_Execute_Authenticator()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"auth": true}""") };
        var authenticator = new TestDummyAuthenticator("Bearer custom-token-123");

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Auth(authenticator);

        // Act
        var response = await builder.GetAsync("/protected");

        // Assert
        Assert.That(_handler.LastRequest!.Headers.Authorization?.ToString(), Is.EqualTo("Bearer custom-token-123"));

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Auth_Generic_Should_Instantiate_Via_DI_And_Execute()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"auth": true}""") };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Auth<TestDummyAuthenticator>("Bearer di-token-456");

        // Act
        var response = await builder.GetAsync("/protected");

        // Assert
        Assert.That(_handler.LastRequest!.Headers.Authorization?.ToString(), Is.EqualTo("Bearer di-token-456"));

        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Builder_ShouldCreateFreshContentForEverySend()
    {
        _handler.ResponseFactory = () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("ok")
        };
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget")
            .Body(new { value = 42 });

        using var first = await builder.PostAsync("/first");
        using var second = await builder.PostAsync("/second");

        Assert.That(_handler.RequestBodies, Is.EqualTo(new[] { "{\"value\":42}", "{\"value\":42}" }));
    }

    [Test]
    public void RequestFailure_ShouldEmitFailureObservation()
    {
        _handler.ExceptionToThrow = new HttpRequestException("Connection failed");
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        Assert.ThrowsAsync<HttpRequestException>(async () => await builder.GetAsync("/unavailable"));

        var observation = _context.RecordedObservations.Single(item => item.Kind == "http.failure");
        var failure = (RestFailureData)observation.Data!;
        Assert.That(failure.RouteTemplate, Is.EqualTo("/unavailable"));
        Assert.That(failure.Message, Is.EqualTo("Connection failed"));
    }
}

// --- Test Helpers ---

public class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public List<string> RequestBodies { get; } = [];
    public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);
    public Func<HttpResponseMessage>? ResponseFactory { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }

        if (request.Content != null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(LastRequestBody);
        }

        return ResponseFactory?.Invoke() ?? ResponseToReturn;
    }
}

public class TestDummyAuthenticator(string token = "Bearer default") : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(ProtoHttpAuthenticationContext context, CancellationToken ct = default)
    {
        context.Request.Headers.TryAddWithoutValidation("Authorization", token);
        return ValueTask.CompletedTask;
    }
}

public sealed class TrackingContent(string content)
    : ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content))
{
    public bool IsDisposed { get; private set; }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
