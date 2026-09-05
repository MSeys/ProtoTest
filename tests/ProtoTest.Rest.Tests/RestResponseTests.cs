namespace ProtoTest.Rest.Tests;

using System.Net;
using NUnit.Framework;

[TestFixture]
public class RestResponseTests
{
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

        var ex = Assert.Throws<InvalidOperationException>(() => response.ShouldHaveStatus(HttpStatusCode.OK));

        Assert.That(ex!.Message, Contains.Substring("Expected HTTP Status 200 (OK), but received 404 (NotFound)"));
        Assert.That(ex.Message, Contains.Substring("Not Found Error"));
    }

    [Test]
    public void ShouldMatchShape_Should_Throw_On_Null_ExpectedShape()
    {
        var rawResponse = new HttpResponseMessage(HttpStatusCode.OK);
        var response = new RestResponse(rawResponse, "{}", TimeSpan.FromMilliseconds(10));

        Assert.Throws<ArgumentNullException>(() => response.ShouldMatchShape(null!));
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