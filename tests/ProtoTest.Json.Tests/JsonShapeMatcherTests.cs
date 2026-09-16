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
    public void AssertMatch_ShouldRespectExplicitJsonPropertyNamesAlongsideNamingPolicy()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false
        };

        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"display_name":"ProtoTest","itemCount":2}""",
            new NamedShape { DisplayName = "ProtoTest", ItemCount = 2 },
            options));
    }

    [Test]
    public void AssertMatch_ShouldCollectMultipleMismatchesInOneException()
    {
        var json = """
        {
            "id": 101,
            "name": "Sven",
            "age": 16,
            "role": "User"
        }
        """;

        var expectedShape = new
        {
            id = 101,
            name = "Matthias",
            age = 25,
            department = "IT"
        };

        var exception = Assert.Throws<JsonShapeMismatchException>(
            () => JsonShapeMatcher.AssertMatch(json, expectedShape));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Mismatches, Has.Count.EqualTo(3));
            Assert.That(exception.MatchedProperties, Does.Contain("$.id"));

            Assert.That(exception.Mismatches[0].PropertyPath, Is.EqualTo("$.name"));
            Assert.That(exception.Mismatches[0].Expected, Is.EqualTo("Matthias"));
            Assert.That(exception.Mismatches[0].Actual, Is.EqualTo("Sven"));

            Assert.That(exception.Mismatches[1].PropertyPath, Is.EqualTo("$.age"));
            Assert.That(exception.Mismatches[1].Expected, Is.EqualTo(25));
            Assert.That(exception.Mismatches[1].Actual, Is.EqualTo(16L));

            Assert.That(exception.Mismatches[2].PropertyPath, Is.EqualTo("$.department"));
            Assert.That(exception.Mismatches[2].Reason, Contains.Substring("Property was missing"));

            Assert.That(exception.Message, Contains.Substring("$.name"));
            Assert.That(exception.Message, Contains.Substring("$.age"));
            Assert.That(exception.Message, Contains.Substring("$.department"));
        });
    }

    [Test]
    public void AssertMatch_ShouldSupportArraysAndDictionaries()
    {
        var expected = new Dictionary<string, object?>
        {
            ["items"] = new object[]
            {
                new Dictionary<string, object?> { ["id"] = 1 },
                new Dictionary<string, object?> { ["id"] = 2 }
            }
        };

        var matched = JsonShapeMatcher.AssertMatch("""{"items":[{"id":1,"extra":true},{"id":2}]}""", expected);

        Assert.That(matched, Contains.Item("$.items[1].id"));
    }

    [Test]
    public void AssertMatch_ShouldHandleCaseInsensitivePropertyNames()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{ "firstName": "Matthias" }""",
            new { FirstName = "Matthias" }));
    }

    [Test]
    public void AssertMatch_ShouldWrapEmptyOrInvalidJsonAsAssertionFailure()
    {
        var expectedShape = new { name = "Matthias" };

        Assert.Throws<JsonDocumentAssertionException>(() => JsonShapeMatcher.AssertMatch("", expectedShape));
        Assert.Throws<JsonDocumentAssertionException>(() => JsonShapeMatcher.AssertMatch("   ", expectedShape));

        var exception = Assert.Throws<JsonDocumentAssertionException>(
            () => JsonShapeMatcher.AssertMatch("not-json", expectedShape));
        Assert.That(exception!.InnerException, Is.InstanceOf<JsonException>());
    }

    private sealed class NamedShape
    {
        [System.Text.Json.Serialization.JsonPropertyName("display_name")]
        public string DisplayName { get; init; } = string.Empty;
        public int ItemCount { get; init; }
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

    [Test]
    public void DiagnosticSerializer_ShouldUseCamelCaseAndRedactSensitiveValues()
    {
        var result = JsonDiagnosticSanitizer.Serialize(new { PropertyPath = "$.user", Token = "secret" });

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.Contain("\"propertyPath\":\"$.user\""));
            Assert.That(result, Does.Contain("\"token\":\"[REDACTED]\""));
            Assert.That(result, Does.Not.Contain("secret"));
        });
    }
}
