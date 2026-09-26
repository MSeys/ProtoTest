namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;
using ProtoTest.Rest.Exceptions;

[TestFixture]
public class RestResponseTests
{
    private ProtoExecutionContext _context = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        _context = new ProtoExecutionContext(
            "TestContext",
            services.BuildServiceProvider().CreateScope(),
            "00001",
            TestMethods.Placeholder
        );
    }

    [TearDown]
    public async Task TearDown()
    {
        await _context.DisposeAsync();
    }

    [Test]
    public void ReadAsDynamic_Should_Parse_Complex_Json_Object()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var json = """
        {
            "id": 100,
            "user": { "name": "Matthias" },
            "tags": ["csharp", "dotnet"]
        }
        """;

        var response = new RestResponse(rawResponse, json, TimeSpan.FromMilliseconds(150));
        dynamic? data = response.ReadAsDynamic();

        Assert.That(data, Is.Not.Null);
        Assert.That(data!.id, Is.EqualTo(100L));
        Assert.That(data.user.name, Is.EqualTo("Matthias"));
        Assert.That(data.tags.Count, Is.EqualTo(2));
        Assert.That(data.tags[0], Is.EqualTo("csharp"));
    }

    [Test]
    public void ReadAsJson_Should_Return_Default_On_Empty_Content()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.NoContent);
        var response = new RestResponse(rawResponse, "", TimeSpan.FromMilliseconds(50));

        var result = response.ReadAsJson<SampleDto>();

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Should_HaveHttpStatus_Should_Throw_When_StatusCode_Mismatches()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        var response = new RestResponse(rawResponse, "Not Found Error", TimeSpan.FromMilliseconds(100));

        var ex = Assert.Throws<RestStatusAssertionException>(() => response.Should.HaveHttpStatus(HttpStatusCode.OK));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.InstanceOf<ProtoAssertionException>());
            Assert.That(ex!.ExpectedStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ex.ActualStatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(ex.ResponseBody, Is.EqualTo("Not Found Error"));
            Assert.That(ex.Negated, Is.False);
            Assert.That(ex.Message, Contains.Substring("Expected HTTP status 200 (OK), but received 404 (NotFound)"));
            Assert.That(ex.Message, Contains.Substring("Not Found Error"));
        }
    }

    [Test]
    public void ShouldNot_HaveHttpStatus_Should_Pass_When_StatusCode_Differs()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        var response = new RestResponse(rawResponse, "Not Found Error", TimeSpan.FromMilliseconds(100));

        response.ShouldNot.HaveHttpStatus(HttpStatusCode.OK);
    }

    [Test]
    public void ShouldNot_HaveHttpStatus_Should_Throw_With_Negated_Message_When_StatusCode_Matches()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var response = new RestResponse(rawResponse, "{}", TimeSpan.FromMilliseconds(100));

        var ex = Assert.Throws<RestStatusAssertionException>(
            () => response.ShouldNot.HaveHttpStatus(HttpStatusCode.OK));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Negated, Is.True);
            Assert.That(ex.ExpectedStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ex.ActualStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ex.Message, Contains.Substring("Expected HTTP status not 200 (OK), but received 200 (OK)"));
        }
    }

    [Test]
    public async Task ShouldNot_HaveHttpStatus_With_Context_Should_Record_The_Failed_Negated_Assertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest negated status", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            "{}",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(Execution: context));

        var exception = Assert.Throws<RestStatusAssertionException>(
            () => response.ShouldNot.HaveHttpStatus(HttpStatusCode.OK));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.http.status");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["expected.status_code"], Is.EqualTo("200"));
            Assert.That(operation.Attributes["actual.status_code"], Is.EqualTo("200"));
            Assert.That(operation.Attributes["assertion.negated"], Is.EqualTo("true"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
            Assert.That(operation.Sections![0].Items![0].Detail, Does.Contain("not"));
        });
        await host.StopAsync();
    }

    [Test]
    public void Should_Assertions_ShouldChainStatusShapeAndStatus()
    {
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"name":"ProtoTest"}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        var returned = response.Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { id = 42 })
            .Should.HaveHttpStatus(HttpStatusCode.OK);

        Assert.That(returned, Is.SameAs(response));
    }

    [Test]
    public void MatchShape_Should_Throw_On_Null_ExpectedShape()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var response = new RestResponse(rawResponse, "{}", TimeSpan.FromMilliseconds(10));

        Assert.Throws<ArgumentNullException>(() => response.Should.MatchShape(null!));
    }

    [Test]
    public void MatchShape_ShouldNameTheRequestIdentifierOnMismatch()
    {
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"name":"ProtoTest"}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        var exception = Assert.Throws<RestAssertionException>(
            () => response.Should.MatchShape(new { id = 7 }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith(
                "GET /orders/42 — Shape mismatch failed with 1 error(s):"));
            Assert.That(exception.Message, Does.Contain("$.id"));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch data stays reachable");
            Assert.That(((JsonShapeMismatchException)exception.InnerException!).Mismatches, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void MatchShape_Should_Record_Observation_When_Context_Is_Provided()
    {
        // Arrange
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var json = """{ "id": 42, "name": "ProtoTest" }""";
        var expectedShape = new { id = 42 };

        var response = new RestResponse(
            rawResponse,
            json,
            TimeSpan.FromMilliseconds(10),
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "MyApiTarget",
                Identifier: "GET /api/test"));

        // Act
        response.Should.MatchShape(expectedShape);

        // Assert
        var hit = _context.RecordedObservations.FirstOrDefault(h => h.Data is RestShapeMatchData);
        Assert.That(hit, Is.Not.Null);
        Assert.That(hit!.TargetName, Is.EqualTo("MyApiTarget"));
        Assert.That(hit.Identifier, Is.EqualTo("GET /api/test"));

        var shapeData = (RestShapeMatchData)hit.Data!;
        Assert.That(shapeData.RequestIdentifier, Is.EqualTo("GET /api/test"));
        Assert.That(shapeData.MatchedProperties, Contains.Item("$.id"));
    }

    [Test]
    public void MatchShape_ShouldUseUniqueAttachmentNamesForRepeatedAssertions()
    {
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"name":"ProtoTest"}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42",
                AttachmentOptions: new ProtoHttpAttachmentOptions(),
                AttachmentPrefix: "rest-01"));

        response.Should.MatchShape(new { id = 42 });
        response.Should.MatchShape(new { name = "ProtoTest" });

        Assert.That(_context.Attachments.Select(item => item.Name), Is.EqualTo(new[]
        {
            "00001-rest-01-expected-shape",
            "00001-rest-01-expected-shape-02"
        }));
    }

    [Test]
    public void Obsolete_ShouldMatchShape_ShouldStillDelegateToTheFacade()
    {
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"name":"ProtoTest"}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        // Intentional: pins the obsolete shim while it delegates to the facade; CS0618 is expected.
#pragma warning disable CS0618
        var returned = response.ShouldMatchShape(new { id = 42 });
        var exception = Assert.Throws<RestAssertionException>(
            () => response.ShouldMatchShape(new { id = 7 }));
#pragma warning restore CS0618

        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(response));
            Assert.That(exception!.Message, Does.StartWith("GET /orders/42 — Shape mismatch"));
        }
    }

    [Test]
    public void ReadAsAnonymous_Should_Map_Json_To_Anonymous_Type_Structure()
    {
        // Arrange
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var json = """
        {
            "id": 42,
            "name": "ProtoTest",
            "isFinished": true
        }
        """;

        var response = new RestResponse(rawResponse, json, TimeSpan.FromMilliseconds(20));
        var template = new { id = 0, name = "", isFinished = false };

        // Act
        var result = response.ReadAsAnonymous(template);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.id, Is.EqualTo(42));
            Assert.That(result.name, Is.EqualTo("ProtoTest"));
            Assert.That(result.isFinished, Is.True);
        }
    }



    [Test]
    public void ReadAsJson_WithPath_ShouldReadSingleValuesAndIndices()
    {
        var response = ReadableResponse(
            """{"id":42,"customer":{"name":"Ada"},"items":[{"sku":"A"},{"sku":"B"}]}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ReadAsJson<int>("$.id"), Is.EqualTo(42));
            Assert.That(response.ReadAsJson<string>("customer.name"), Is.EqualTo("Ada"));
            Assert.That(response.ReadAsJson<string>("$.items[1].sku"), Is.EqualTo("B"));
        }
    }

    [Test]
    public void ReadAsJson_WithPath_ShouldPreserveDecimalPrecision()
    {
        var response = ReadableResponse("""{"amount":123456789.123456789}""");

        var amount = response.ReadAsJson<decimal>("$.amount");

        Assert.That(amount, Is.EqualTo(123456789.123456789m));
    }

    [Test]
    public void ReadAsJson_WithPath_ShouldReturnNullForJsonNull()
    {
        var response = ReadableResponse("""{"note":null}""");

        Assert.That(response.ReadAsJson<string?>("$.note"), Is.Null);
    }

    [Test]
    public void ReadAsJson_WithPath_ShouldThrowOnWrongType()
    {
        var response = ReadableResponse("""{"name":"ProtoTest"}""");

        Assert.Throws<JsonException>(() => response.ReadAsJson<int>("$.name"));
    }

    [Test]
    public void ReadAsJson_WithPath_ShouldNameTheIdentifierAndThePathWhenMissing()
    {
        var response = ReadableResponse("""{"id":42}""");

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadAsJson<int>("$.missing"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith(
                "GET /orders/42 — The JSON path '$.missing' did not match: the member 'missing' was not found."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonPathException>());
        }
    }

    [Test]
    public void ReadRequired_ShouldReturnTheValue()
    {
        var response = ReadableResponse("""{"id":42,"name":"ProtoTest"}""");

        var dto = response.ReadRequired<SampleDto>();

        Assert.That(dto.Id, Is.EqualTo(42));
    }

    [Test]
    public void ReadRequired_ShouldThrowNamingTheIdentifierOnAnEmptyBody()
    {
        var response = ReadableResponse(string.Empty);

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<SampleDto>());

        Assert.That(exception!.Message, Is.EqualTo(
            "GET /orders/42 — ReadRequired<SampleDto> failed: the response body was empty."));
    }

    [Test]
    public void ReadRequired_ShouldThrowOnAJsonNullBody()
    {
        var response = ReadableResponse("null");

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<SampleDto>());

        Assert.That(exception!.Message, Does.Contain("the response body was JSON null"));
    }

    [Test]
    public void ReadRequired_ShouldThrowOnAJsonNullBodyForAValueType()
    {
        var response = ReadableResponse("null");

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<int>());

        Assert.That(exception!.Message, Is.EqualTo(
            "GET /orders/42 — ReadRequired<Int32> failed: the response body was JSON null."));
    }

    [Test]
    public void ReadRequired_WithPath_ShouldComposeWithThePathRead()
    {
        var response = ReadableResponse("""{"id":42,"customer":{"id":null}}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ReadRequired<int>("$.id"), Is.EqualTo(42));
            Assert.That(
                Assert.Throws<RestAssertionException>(() => response.ReadRequired<string>("$.customer.id"))!.Message,
                Does.Contain("$.customer.id"));
            Assert.That(
                Assert.Throws<RestAssertionException>(() => response.ReadRequired<string>("$.customer.name"))!.Message,
                Does.Contain("the member 'name' was not found"));
        }
    }

    [Test]
    public void ReadRequired_WithPath_ShouldThrowOnAJsonNullValueForAValueType()
    {
        var response = ReadableResponse("""{"note":null}""");

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<int>("$.note"));

        Assert.That(exception!.Message, Is.EqualTo(
            "GET /orders/42 — ReadRequired<Int32>('$.note') failed: the value at '$.note' was JSON null."));
    }

    [Test]
    public void ReadRequired_ShouldChainWithShapeAndStatus()
    {
        var response = ReadableResponse("""{"id":42}""");

        var id = response.Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new { id = 42 })
            .ReadRequired<int>("$.id");

        Assert.That(id, Is.EqualTo(42));
    }

    [Test]
    public async Task ReadRequired_ShouldRecordTheFailedDeserializeEventOnAnEmptyBody()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest required empty trace", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            string.Empty,
            TimeSpan.Zero,
            new ProtoHttpResponseContext(Execution: context));

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<SampleDto>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "http.response.deserialize");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(entry.Attributes["target.type"], Is.EqualTo(typeof(SampleDto).FullName));
            Assert.That(entry.Attributes.ContainsKey("json.path"), Is.False);
            Assert.That(entry.Error!.Type, Is.EqualTo(typeof(RestAssertionException).FullName));
            Assert.That(entry.Error!.Message, Does.Contain("the response body was empty"));
        }
        await host.StopAsync();
    }

    [Test]
    public async Task ReadRequired_WithPath_ShouldRecordTheFailedDeserializeEventWhenThePathIsMissing()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rest required path trace", TestMethods.Placeholder);
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42}""",
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

        var exception = Assert.Throws<RestAssertionException>(() => response.ReadRequired<int>("$.missing"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "http.response.deserialize");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(entry.Attributes["target.type"], Is.EqualTo(typeof(int).FullName));
            Assert.That(entry.Attributes["json.path"], Is.EqualTo("$.missing"));
            Assert.That(entry.Error!.Type, Is.EqualTo(typeof(RestAssertionException).FullName));
            Assert.That(entry.Error!.Message, Does.StartWith(
                "GET /orders/42 — The JSON path '$.missing' did not match"));
        }
        await host.StopAsync();
    }

    private RestResponse ReadableResponse(string json)
        => new(
            new HttpResponseMessage(HttpStatusCode.OK),
            json,
            TimeSpan.Zero,
            new ProtoHttpResponseContext(
                Execution: _context,
                TargetName: "Orders",
                Identifier: "GET /orders/42"));

    private class SampleDto
    {
        public int Id { get; set; }
    }
}
