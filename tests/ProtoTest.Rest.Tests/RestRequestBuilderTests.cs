namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;

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

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null)
            .Header("X-Custom-Header", "TestValue")
            .Header("User-Agent", "ProtoTest-Runner");

        // Act
        var response = await builder.GetAsync("/users/{id}", new { id = 42, active = true });

        // Assert - Request Verification
        Assert.That(_handler.LastRequest, Is.Not.Null);
        Assert.That(_handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Get));
        Assert.That(_handler.LastRequest.RequestUri?.ToString(), Is.EqualTo("https://api.prototest.dev/users/42?active=True"));
        Assert.That(_handler.LastRequest.Headers.GetValues("X-Custom-Header").Single(), Is.EqualTo("TestValue"));
        Assert.That(_handler.LastRequest.Headers.GetValues("User-Agent").Single(), Is.EqualTo("ProtoTest-Runner"));

        // Assert - Response Verification via RestResponse Helpers
        response.ShouldHaveStatus(HttpStatusCode.OK);

        var body = response.ReadAsAnonymous(new { status = "" });
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.status, Is.EqualTo("ok"));
    }

    [Test]
    public async Task SendAsync_Should_Emit_CoverageHit_With_RestHitData()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id": 1}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null);

        // Act
        await builder.GetAsync("/users/{id}", new { id = 1 });

        // Assert - Context Recorded Hits
        var hit = _context.RecordedHits.FirstOrDefault(h => h.Data is RestHitData);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.TargetName, Is.EqualTo("TestTarget"));
        Assert.That(hit.Identifier, Is.EqualTo("GET /users/{id}"));

        var data = (RestHitData)hit.Data!;
        Assert.That(data.Method, Is.EqualTo("GET"));
        Assert.That(data.RouteTemplate, Is.EqualTo("/users/{id}"));
        Assert.That(data.StatusCode, Is.EqualTo(200));
        Assert.That(data.ResponseBody, Is.EqualTo("""{"id": 1}"""));
    }

    [Test]
    public async Task Body_Object_Should_Serialize_As_Json_Content_And_Emit_ShapeMatchData()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id": 1, "created": true}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null)
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
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { id = 1, created = true });

        var shapeHit = _context.RecordedHits.FirstOrDefault(h => h.Data is ShapeMatchData);
        Assert.That(shapeHit, Is.Not.Null);

        var shapeData = (ShapeMatchData)shapeHit!.Data!;
        Assert.That(shapeData.MatchedProperties, Contains.Item("$.id"));
        Assert.That(shapeData.MatchedProperties, Contains.Item("$.created"));
    }

    [Test]
    public async Task Body_RawString_Should_Set_Custom_MediaType()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<response>ok</response>", System.Text.Encoding.UTF8, "application/xml")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null)
            .Body("<xml><user>Matthias</user></xml>", "application/xml");

        // Act
        var response = await builder.PostAsync("/xml-endpoint");

        // Assert - Request & Response Verification
        Assert.That(_handler.LastRequest!.Content!.Headers.ContentType?.MediaType, Is.EqualTo("application/xml"));
        Assert.That(_handler.LastRequestBody, Is.EqualTo("<xml><user>Matthias</user></xml>"));

        response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.That(response.Content, Is.EqualTo("<response>ok</response>"));
    }

    [Test]
    public async Task Auth_Instance_Should_Execute_Authenticator()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"auth": true}""") };
        var authenticator = new TestDummyAuthenticator("Bearer custom-token-123");

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null)
            .Auth(authenticator);

        // Act
        var response = await builder.GetAsync("/protected");

        // Assert
        Assert.That(_handler.LastRequest!.Headers.Authorization?.ToString(), Is.EqualTo("Bearer custom-token-123"));

        response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task Auth_Generic_Should_Instantiate_Via_DI_And_Execute()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"auth": true}""") };

        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget", null)
            .Auth<TestDummyAuthenticator>("Bearer di-token-456");

        // Act
        var response = await builder.GetAsync("/protected");

        // Assert
        Assert.That(_handler.LastRequest!.Headers.Authorization?.ToString(), Is.EqualTo("Bearer di-token-456"));

        response.ShouldHaveStatus(HttpStatusCode.OK);
        Assert.That(response.IsSuccessStatusCode, Is.True);
    }
}

// --- Test Helpers ---

public class TestHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }
    public HttpResponseMessage ResponseToReturn { get; set; } = new(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        if (request.Content != null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return ResponseToReturn;
    }
}

public class TestDummyAuthenticator(string token = "Bearer default") : IRestAuthenticator
{
    public ValueTask AuthenticateAsync(HttpRequestMessage request, CancellationToken ct = default)
    {
        request.Headers.TryAddWithoutValidation("Authorization", token);
        return ValueTask.CompletedTask;
    }
}
