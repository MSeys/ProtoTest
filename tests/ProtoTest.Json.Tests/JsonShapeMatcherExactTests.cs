namespace ProtoTest.Json.Tests;

using System.Text.Json;

[TestFixture]
public sealed class JsonShapeMatcherExactTests
{
    [Test]
    public void AssertExactMatch_ShouldAcceptWhenEveryFieldIsMentioned()
    {
        var matched = JsonShapeMatcher.AssertExactMatch(
            """{"items":[{"sku":"A","qty":1},{"sku":"B","qty":2}],"state":"Active"}""",
            new
            {
                items = new[]
                {
                    new { sku = "A", qty = 1 },
                    new { sku = "B", qty = 2 }
                },
                state = "Active"
            });

        Assert.Multiple(() =>
        {
            Assert.That(matched, Does.Contain("$.items[1].qty"));
            Assert.That(matched, Does.Contain("$.state"));
        });
    }

    [Test]
    public void AssertExactMatch_ShouldNameEveryUnmentionedField()
    {
        var exception = Assert.Throws<JsonShapeMismatchException>(() => JsonShapeMatcher.AssertExactMatch(
            """{"id":42,"name":"ProtoTest","customer":{"email":"ada@example.test"}}""",
            new Dictionary<string, object?>
            {
                ["id"] = 42,
                ["customer"] = new Dictionary<string, object?>()
            }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Mismatches.Select(mismatch => mismatch.PropertyPath),
                Is.EqualTo(new[] { "$.name", "$.customer.email" }));
            Assert.That(exception.Mismatches.Select(mismatch => mismatch.Reason),
                Is.All.EqualTo("Property was not mentioned in the expected shape."));
            Assert.That(exception.Mismatches[0].Actual, Is.EqualTo("ProtoTest"));
            Assert.That(exception.Message, Does.Contain("$.name"));
            Assert.That(exception.Message, Does.Contain("$.customer.email"));
            Assert.That(exception.MatchedProperties, Does.Contain("$.id"));
        }
    }

    [Test]
    public void AssertExactMatch_ShouldReportTheShallowestUnmentionedBranch()
    {
        var exception = Assert.Throws<JsonShapeMismatchException>(() => JsonShapeMatcher.AssertExactMatch(
            """{"id":42,"extra":{"nested":{"deep":true}}}""",
            new { id = 42 }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Mismatches.Select(mismatch => mismatch.PropertyPath),
                Is.EqualTo(new[] { "$.extra" }),
                "one unmentioned branch reports its shallowest path, not every leaf below it");
            Assert.That(exception.Mismatches[0].Actual, Is.EqualTo("""{"nested":{"deep":true}}"""));
        });
    }

    [Test]
    public void AssertExactMatch_ShouldTreatAValueConstraintAsMentioningItsWholeSubtree()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertExactMatch(
            """{"id":42,"customer":{"email":"ada@example.test","phone":"555"}}""",
            new { id = 42, customer = JsonValue.Any() }));
    }

    [Test]
    public void AssertExactMatch_ShouldMatchPropertyNamesCaseInsensitively()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertExactMatch(
            """{"ID":42}""",
            new { id = 42 }));

        var exception = Assert.Throws<JsonShapeMismatchException>(() => JsonShapeMatcher.AssertExactMatch(
            """{"ID":42}""",
            new { id = 42 },
            new JsonSerializerOptions { PropertyNameCaseInsensitive = false }));

        Assert.That(exception!.Mismatches.Select(mismatch => mismatch.PropertyPath),
            Does.Contain("$.ID"),
            "a strict comparison reports the differently-cased field as unmentioned");
    }

    [Test]
    public void AssertExactMatch_ShouldReportUnmentionedFieldsAlongsideOtherMismatches()
    {
        var exception = Assert.Throws<JsonShapeMismatchException>(() => JsonShapeMatcher.AssertExactMatch(
            """{"id":7,"extra":true}""",
            new { id = 42 }));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Mismatches, Has.Count.EqualTo(2));
            Assert.That(exception.Mismatches.Any(mismatch => mismatch.PropertyPath == "$.id"
                && mismatch.Reason == "Values did not match."), Is.True);
            Assert.That(exception.Mismatches.Any(mismatch => mismatch.PropertyPath == "$.extra"
                && mismatch.Reason == "Property was not mentioned in the expected shape."), Is.True);
        });
    }

    [Test]
    public void AssertExactMatch_ShouldRejectAnEmptyOrInvalidBodyLikeThePartialMatch()
    {
        Assert.Throws<JsonDocumentAssertionException>(
            () => JsonShapeMatcher.AssertExactMatch("", new { id = 1 }));
        Assert.Throws<JsonDocumentAssertionException>(
            () => JsonShapeMatcher.AssertExactMatch("not-json", new { id = 1 }));
    }

    [Test]
    public void AssertMatch_ShouldKeepIgnoringExtraFieldsWithoutExact()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"id":42,"name":"ProtoTest"}""",
            new { id = 42 }));
    }

    [Test]
    public void FindUnmentionedFields_ShouldReportFieldsTheMentionedPathsDoNotCover()
    {
        var unmentioned = JsonShapeMatcher.FindUnmentionedFields(
            """{"id":42,"name":"ProtoTest","customer":{"id":7,"email":"ada@example.test"}}""",
            ["$", "$.id", "$.customer", "$.customer.id"]);

        Assert.That(unmentioned.Select(field => field.PropertyPath),
            Is.EqualTo(new[] { "$.name", "$.customer.email" }));
    }

    [Test]
    public void FindUnmentionedFields_ShouldReportTheShallowestBranchWhenNothingIsMentioned()
    {
        var unmentioned = JsonShapeMatcher.FindUnmentionedFields(
            """{"id":42,"customer":{"email":"ada@example.test"},"tags":["a","b"]}""",
            []);

        Assert.That(unmentioned.Select(field => field.PropertyPath),
            Is.EqualTo(new[] { "$.id", "$.customer", "$.tags" }));
    }

    [Test]
    public void FindUnmentionedFields_ShouldFollowTheCaseInsensitivePropertyRule()
    {
        Assert.That(
            JsonShapeMatcher.FindUnmentionedFields("""{"ID":42}""", ["$.id"]),
            Is.Empty);

        Assert.That(
            JsonShapeMatcher.FindUnmentionedFields(
                """{"ID":42}""",
                ["$.id"],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = false })
                .Single().PropertyPath,
            Is.EqualTo("$.ID"));
    }

    [Test]
    public void FindUnmentionedFields_ShouldReportNothingForEmptyContentAndThrowOnInvalidJson()
    {
        Assert.That(JsonShapeMatcher.FindUnmentionedFields("", ["$.id"]), Is.Empty);
        Assert.Throws<JsonDocumentAssertionException>(
            () => JsonShapeMatcher.FindUnmentionedFields("not-json", ["$.id"]));
    }
}
