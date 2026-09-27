namespace ProtoTest.Rest.Tests;

using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Exceptions;

[TestFixture]
public sealed class RestShapeExpectationTests
{
    private ProtoExecutionContext _context = null!;
    private TestHttpMessageHandler _handler = null!;
    private HttpClient _httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        _context = new ProtoExecutionContext(
            "TestContext",
            services.BuildServiceProvider().CreateScope(),
            "00001",
            TestMethods.Placeholder);
        _handler = new TestHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://api.prototest.dev") };
    }

    [TearDown]
    public async Task TearDown()
    {
        _handler.Dispose();
        _httpClient.Dispose();
        await _context.DisposeAsync();
    }

    [Test]
    public void MatchShape_Exact_ShouldRejectFieldsTheShapeDoesNotMention()
    {
        var response = Response("""{"id":42,"name":"ProtoTest","customer":{"email":"ada@example.test"}}""");

        response.Should.MatchShape(new { id = 42 });

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.MatchShape(new { id = 42 }, exact: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith(
                "GET /orders/42 — Shape mismatch failed with 2 error(s):"));
            Assert.That(exception.Message, Does.Contain("$.name"));
            Assert.That(exception.Message, Does.Contain("$.customer"));
            Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>());
            Assert.That(
                ((JsonShapeMismatchException)exception.InnerException!).Mismatches.Select(mismatch => mismatch.PropertyPath),
                Is.EqualTo(new[] { "$.name", "$.customer" }));
        }
    }

    [Test]
    public void MatchShape_Exact_ShouldPassWhenEveryFieldIsMentioned()
    {
        var response = Response("""{"id":42,"customer":{"email":"ada@example.test"}}""");

        var returned = response.Should.MatchShape(
            new { id = 42, customer = new { email = JsonValue.StringContaining("@example.test") } },
            exact: true);

        Assert.That(returned, Is.SameAs(response));
    }

    [Test]
    public void MatchShape_Exact_ShouldTreatAValueConstraintAsMentioningItsSubtree()
    {
        var response = Response("""{"id":42,"customer":{"email":"ada@example.test","phone":"555"}}""");

        response.Should.MatchShape(new { id = 42, customer = JsonValue.NotNull() }, exact: true);
    }

    [Test]
    public void MatchShape_Exact_ShouldKeepIgnoringExtraFieldsWithoutExact()
    {
        var response = Response("""{"id":42,"extra":true}""");

        Assert.DoesNotThrow(() => response.Should.MatchShape(new { id = 42 }));
    }

    [Test]
    public async Task MatchShape_Exact_ShouldRecordTheExactFlagAndUnmentionedFieldsInTheTrace()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest exact shape", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"extra":true}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.MatchShape(new { id = 42 }, exact: true));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["shape.exact"], Is.EqualTo("true"));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Attributes["shape.mismatches"], Does.Contain("Property was not mentioned"));
            Assert.That(operation.Attributes["shape.mismatches"], Does.Contain("$.extra"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_WithoutExact_ShouldNotRecordTheExactAttribute()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest partial shape", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(Execution: context, TargetName: "Orders", Identifier: "GET /orders/42"));

        response.Should.MatchShape(new { id = 42 });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.That(operation.Attributes.ContainsKey("shape.exact"), Is.False);
        await host.StopAsync();
    }

    [Test]
    public async Task ExpectAsync_ShouldMatchTheShapeInTheCallAndRecordTheObservation()
    {
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent("""{"id":1,"created":true}""")
        };
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        using var response = await builder.PostAsync("/users").ExpectAsync(new { id = JsonValue.GreaterThan(0) });

        var shape = _context.RecordedObservations.Single(item => item.Data is RestShapeMatchData);
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
            Assert.That(((RestShapeMatchData)shape.Data!).MatchedProperties, Does.Contain("$.id"));
        });
    }

    [Test]
    public void ExpectAsync_ShouldDisposeTheResponseAndRethrowWhenTheShapeDoesNotMatch()
    {
        var raw = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":1}""") };
        _handler.ResponseToReturn = raw;
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        var exception = Assert.ThrowsAsync<RestAssertionException>(
            async () => await builder.GetAsync("/users").ExpectAsync(new { id = 2 }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith("GET /users — Shape mismatch failed with 1 error(s):"));
            Assert.That(exception.Message, Does.Contain("$.id"));
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await raw.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public void ExpectAsync_Exact_ShouldFailNamingTheUnmentionedFields()
    {
        _handler.ResponseToReturn = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"id":1,"extra":true}""")
        };
        var builder = new RestRequestBuilder(_httpClient, _context, "TestTarget");

        var exception = Assert.ThrowsAsync<RestAssertionException>(
            async () => await builder.GetAsync("/users").ExpectAsync(new { id = 1 }, exact: true));

        Assert.That(exception!.Message, Does.Contain("$.extra"));
        Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
    }

    private RestResponse Response(string json)
        => new(
            new HttpResponseMessage(HttpStatusCode.OK),
            json,
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));
}
