namespace ProtoTest.Rest.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
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
            (MethodInfo)MethodInfo.GetCurrentMethod()!
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
    public void ShouldHaveStatus_Should_Throw_When_StatusCode_Mismatches()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.NotFound);
        var response = new RestResponse(rawResponse, "Not Found Error", TimeSpan.FromMilliseconds(100));

        var ex = Assert.Throws<RestStatusAssertionException>(() => response.ShouldHaveStatus(HttpStatusCode.OK));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.InstanceOf<ProtoAssertionException>());
            Assert.That(ex!.ExpectedStatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(ex.ActualStatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(ex.ResponseBody, Is.EqualTo("Not Found Error"));
            Assert.That(ex.Message, Contains.Substring("Expected HTTP status 200 (OK), but received 404 (NotFound)"));
            Assert.That(ex.Message, Contains.Substring("Not Found Error"));
        }
    }

    [Test]
    public void ShouldMatchShape_Should_Throw_On_Null_ExpectedShape()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var response = new RestResponse(rawResponse, "{}", TimeSpan.FromMilliseconds(10));

        Assert.Throws<ArgumentNullException>(() => response.ShouldMatchShape(null!));
    }

    [Test]
    public void ShouldMatchShape_Should_Record_Observation_When_Context_Is_Provided()
    {
        // Arrange
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var json = """{ "id": 42, "name": "ProtoTest" }""";
        var expectedShape = new { id = 42 };

        var response = new RestResponse(
            rawResponse,
            json,
            TimeSpan.FromMilliseconds(10),
            context: _context,
            targetName: "MyApiTarget",
            routeIdentifier: "GET /api/test"
        );

        // Act
        response.ShouldMatchShape(expectedShape);

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
    public void ShouldMatchShape_ShouldUseUniqueAttachmentNamesForRepeatedAssertions()
    {
        var response = new RestResponse(
            new HttpResponseMessage(HttpStatusCode.OK),
            """{"id":42,"name":"ProtoTest"}""",
            TimeSpan.Zero,
            _context,
            "Orders",
            "GET /orders/42",
            new RestAttachmentOptions(),
            "rest-01");

        response.ShouldMatchShape(new { id = 42 });
        response.ShouldMatchShape(new { name = "ProtoTest" });

        Assert.That(_context.Attachments.Select(item => item.Name), Is.EqualTo(new[]
        {
            "00001-rest-01-expected-shape",
            "00001-rest-01-expected-shape-02"
        }));
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

    private class SampleDto
    {
        public int Id { get; set; }
    }
}
