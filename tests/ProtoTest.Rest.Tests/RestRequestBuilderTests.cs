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
        _context = new ProtoExecutionContext("", services.BuildServiceProvider().CreateScope(), "", (MethodInfo)MethodInfo.GetCurrentMethod()!);
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

        var builder = new RestRequestBuilder(_httpClient, _context, null)
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
    public async Task Body_Object_Should_Serialize_As_Json_Content()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id": 1, "created": true}""")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, null)
            .Body(new { name = "Matthias", role = "Admin" });

        // Act
        var response = await builder.PostAsync("/users");

        // Assert - Request Verification
        Assert.That(_handler.LastRequest?.Content, Is.Not.Null);
        Assert.That(_handler.LastRequest!.Content!.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));

        Assert.That(_handler.LastRequestBody, Is.Not.Null);
        Assert.That(_handler.LastRequestBody, Contains.Substring("Matthias"));

        // Assert - Response Verification via Fluent Chaining
        response
            .ShouldHaveStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { id = 1, created = true });
    }

    [Test]
    public async Task Body_RawString_Should_Set_Custom_MediaType()
    {
        // Arrange
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<response>ok</response>", System.Text.Encoding.UTF8, "application/xml")
        };

        var builder = new RestRequestBuilder(_httpClient, _context, null)
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

        var builder = new RestRequestBuilder(_httpClient, _context, null)
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

        var builder = new RestRequestBuilder(_httpClient, _context, null)
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