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
    public void AssertMatch_ShouldIgnorePropertiesMarkedWithJsonIgnore()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"display_name":"Proto"}""",
            new IgnoredShape { DisplayName = "Proto", Secret = "not-present-in-json" }));
    }

    private sealed class IgnoredShape
    {
        [System.Text.Json.Serialization.JsonPropertyName("display_name")]
        public string DisplayName { get; init; } = string.Empty;

        [System.Text.Json.Serialization.JsonIgnore]
        public string Secret { get; init; } = string.Empty;
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

    [Test]
    public void DiagnosticSanitizer_ShouldRedactSensitiveKeysInNonJsonBodies()
    {
        var form = JsonDiagnosticSanitizer.Sanitize("username=ada&password=hunter2");
        var xml = JsonDiagnosticSanitizer.Sanitize(
            """<login><username>ada</username><password>hunter2</password></login>""");
        var multipart = JsonDiagnosticSanitizer.Sanitize(
            "--b\r\nContent-Disposition: form-data; name=\"username\"\r\n\r\nada\r\n" +
            "--b\r\nContent-Disposition: form-data; name=\"password\"\r\n\r\nhunter2\r\n--b--");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(form, Is.EqualTo("username=ada&password=[REDACTED]"));
            Assert.That(xml, Does.Not.Contain("hunter2"));
            Assert.That(xml, Does.Contain("<password>[REDACTED]</password>"));
            Assert.That(xml, Does.Contain("<username>ada</username>"));
            Assert.That(multipart, Does.Not.Contain("hunter2"));
            Assert.That(multipart, Does.Contain("ada"));
            Assert.That(multipart, Does.Contain("password\"\r\n\r\n[REDACTED]"));
        }
    }

    [Test]
    public void DiagnosticSanitizer_ShouldRedactDuplicateSensitiveKeysInsteadOfThrowing()
    {
        var result = JsonDiagnosticSanitizer.Sanitize("""{"token":"a","user":"ada","token":"b"}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Does.Not.Contain("\"a\""));
            Assert.That(result, Does.Not.Contain("\"b\""));
            Assert.That(result, Does.Contain("\"token\":\"[REDACTED]\""));
            Assert.That(result, Does.Contain("\"user\":\"ada\""));
        }
    }

    [Test]
    public void DiagnosticSanitizer_ShouldRedactMultiLineAndSingleQuotedMultipartParts()
    {
        var multiLine = JsonDiagnosticSanitizer.Sanitize(
            "--b\r\nContent-Disposition: form-data; name=\"password\"\r\n\r\nline one\r\nline two\r\n--b--");
        var singleQuoted = JsonDiagnosticSanitizer.Sanitize(
            "--b\r\nContent-Disposition: form-data; name='password'\r\n\r\nhunter2\r\n--b--");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(multiLine, Does.Not.Contain("line one"));
            Assert.That(multiLine, Does.Not.Contain("line two"));
            Assert.That(multiLine, Does.Contain("[REDACTED]"));
            Assert.That(multiLine, Does.Contain("--b--"));
            Assert.That(singleQuoted, Does.Not.Contain("hunter2"));
            Assert.That(singleQuoted, Does.Contain("[REDACTED]"));
        }
    }

    [Test]
    public void DiagnosticSanitizer_ShouldRedactXmlElementsWithoutOverMatching()
    {
        var nested = JsonDiagnosticSanitizer.Sanitize(
            """<login><password><hint>hunter2</hint></password><user>ada</user></login>""");
        var attributeValue = JsonDiagnosticSanitizer.Sanitize(
            """<login note="token=hunter2"><user>ada</user></login>""");

        using (Assert.EnterMultipleScope())
        {
            // The nested hint is inside a sensitive element, so its text is redacted but its markup stays.
            Assert.That(nested, Does.Not.Contain("hunter2"));
            Assert.That(nested, Does.Contain("<hint>[REDACTED]</hint>"));
            Assert.That(nested, Does.Contain("<user>ada</user>"));
            // "token=hunter2" is the value of the unrelated "note" attribute and must not be rewritten.
            Assert.That(attributeValue, Does.Contain("note=\"token=hunter2\""));
            Assert.That(attributeValue, Does.Contain("<user>ada</user>"));
        }
    }

    [Test]
    public void DiagnosticSanitizer_ShouldRedactXmlAttributesAndElementValues()
    {
        var attribute = JsonDiagnosticSanitizer.Sanitize("""<login token="hunter2"><user>ada</user></login>""");
        var element = JsonDiagnosticSanitizer.Sanitize("""<login><token>hunter2</token></login>""");
        var markupInAttribute = JsonDiagnosticSanitizer.Sanitize(
            "<login note=\"<token>hunter2</token>\"><user>ada</user></login>");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attribute, Is.EqualTo("""<login token="[REDACTED]"><user>ada</user></login>"""));
            Assert.That(element, Is.EqualTo("<login><token>[REDACTED]</token></login>"));
            // Markup inside a quoted attribute value is data, not an element: it stays untouched.
            Assert.That(markupInAttribute, Does.Contain("note=\"<token>hunter2</token>\""));
        }
    }

    [Test]
    public void ShapeMatcher_ShouldTreatNonStringDictionaryKeysAsStringifiedObjectNames()
    {
        var matched = JsonShapeMatcher.AssertMatch(
            """{"1":"one","2":"two"}""",
            new Dictionary<int, object?> { [1] = "one", [2] = "two" });

        Assert.That(matched, Does.Contain("$.1"));
    }

    [Test]
    public void DiagnosticSanitizer_ShouldTruncateBodiesLongerThanTheConfiguredLimit()
    {
        var options = new JsonDiagnosticOptions { MaxDiagnosticBodyLength = 10 };
        var body = new string('x', 25);

        var result = JsonDiagnosticSanitizer.Sanitize(body, options);

        Assert.Multiple(() =>
        {
            Assert.That(result, Does.StartWith("xxxxxxxxxx"));
            Assert.That(result, Does.Contain("15 characters truncated"));
            Assert.That(result, Does.Not.Contain(body));
        });
    }

    [Test]
    public void DiagnosticSanitizer_ShouldKeepBodiesIntactWhenTruncationIsDisabled()
    {
        var options = new JsonDiagnosticOptions { MaxDiagnosticBodyLength = 10 };
        var body = new string('x', 25);

        var result = JsonDiagnosticSanitizer.Sanitize(body, options, truncate: false);

        Assert.That(result, Is.EqualTo(body));
    }
}
