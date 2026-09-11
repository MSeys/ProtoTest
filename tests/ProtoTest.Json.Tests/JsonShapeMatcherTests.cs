namespace ProtoTest.Json.Tests;

using System.Text.Json;

[TestFixture]
public sealed class JsonShapeMatcherTests
{
    private enum State { Active, Disabled }

    [Test]
    public void AssertMatch_ShouldSupportObjectsArraysAndReusableValueMatchers()
    {
        var matched = JsonShapeMatcher.AssertMatch(
            """{"items":[{"id":"7b2c5c91-83be-43bc-8db3-9082afefbb43","name":"Notebook","price":12.5}],"state":"Active"}""",
            new
            {
                items = new[]
                {
                    new
                    {
                        id = Guid.Parse("7b2c5c91-83be-43bc-8db3-9082afefbb43"),
                        name = JsonValue.StringContaining("book"),
                        price = JsonValue.Between(10m, 20m)
                    }
                },
                state = State.Active
            });

        Assert.That(matched, Does.Contain("$.items[0].price"));
    }

    [Test]
    public void LessThan_ShouldNotAcceptAnIncompatibleValue()
    {
        var exception = Assert.Throws<JsonShapeMismatchException>(() =>
            JsonShapeMatcher.AssertMatch("""{"value":"not-a-number"}""", new { value = JsonValue.LessThan(10) }));

        Assert.That(exception!.Mismatches.Single().PropertyPath, Is.EqualTo("$.value"));
    }

    [Test]
    public void AssertMatch_ShouldHonorJsonPropertyNaming()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"display_name":"Proto"}""",
            new { DisplayName = "Proto" },
            options));
    }

    [Test]
    public void DiagnosticSanitizer_ShouldRedactNestedPropertiesAndPreserveUnchangedJson()
    {
        Assert.Multiple(() =>
        {
            Assert.That(JsonDiagnosticSanitizer.Sanitize("""{"nested":{"token":"secret"}}"""),
                Is.EqualTo("""{"nested":{"token":"[REDACTED]"}}"""));
            Assert.That(JsonDiagnosticSanitizer.Sanitize("""{"id": 1}"""),
                Is.EqualTo("""{"id": 1}"""));
        });
    }
}
