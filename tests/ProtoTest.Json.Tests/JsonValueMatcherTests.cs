namespace ProtoTest.Json.Tests;

using System.Text.RegularExpressions;

[TestFixture]
public sealed class JsonValueMatcherTests
{
    [Test]
    public void Any_ShouldAcceptAnyPresentValue()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch("""{"value":null}""", new { value = JsonValue.Any() }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch("""{"value":42}""", new { value = JsonValue.Any() }));

        var mismatch = Mismatch(JsonValue.Any(), """{"other":1}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Reason, Does.Contain("Property was missing"));
        });
    }

    [Test]
    public void Null_ShouldOnlyAcceptJsonNull()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch("""{"value":null}""", new { value = JsonValue.Null() }));

        var mismatch = Mismatch(JsonValue.Null(), """{"value":"text"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("null"));
            Assert.That(mismatch.Actual, Is.EqualTo("text"));
            Assert.That(mismatch.Reason, Does.Contain("Expected null"));
        });
    }

    [Test]
    public void Regex_ShouldMatchThePatternAndHonorOptions()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"inv-0042"}""", new { value = JsonValue.Regex(@"^inv-\d{4}$") }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"INV-0042"}""", new { value = JsonValue.Regex(@"^inv-\d{4}$", RegexOptions.IgnoreCase) }));

        var mismatch = Mismatch(JsonValue.Regex(@"^inv-\d{4}$"), """{"value":"inv-42"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo(@"matches /^inv-\d{4}$/"));
            Assert.That(mismatch.Actual, Is.EqualTo("inv-42"));
        });
    }

    [Test]
    public void StringStartingWith_ShouldMatchThePrefixAndRespectComparison()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"ProtoTest"}""", new { value = JsonValue.StringStartingWith("Proto") }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"prototest"}""", new { value = JsonValue.StringStartingWith("PROTO", StringComparison.OrdinalIgnoreCase) }));

        var mismatch = Mismatch(JsonValue.StringStartingWith("Proto"), """{"value":"TestProto"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("starts with \"Proto\""));
            Assert.That(mismatch.Actual, Is.EqualTo("TestProto"));
        });
    }

    [Test]
    public void StringEndingWith_ShouldMatchTheSuffixAndRespectComparison()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"invoice.test"}""", new { value = JsonValue.StringEndingWith(".test") }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"invoice.TEST"}""", new { value = JsonValue.StringEndingWith(".test", StringComparison.OrdinalIgnoreCase) }));

        var mismatch = Mismatch(JsonValue.StringEndingWith(".test"), """{"value":"invoice.tests"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("ends with \".test\""));
            Assert.That(mismatch.Actual, Is.EqualTo("invoice.tests"));
        });
    }

    [Test]
    public void StringMatching_ShouldApplyThePredicateToStringsOnly()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"abcd"}""",
            new { value = JsonValue.StringMatching(value => value?.Length == 4, "a four character string") }));

        // Anything that is not a string reaches the predicate as null.
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":42}""",
            new { value = JsonValue.StringMatching(value => value is null, "not a string") }));

        var mismatch = Mismatch(
            JsonValue.StringMatching(value => value?.Length == 4, "a four character string"),
            """{"value":"abc"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("a four character string"));
            Assert.That(mismatch.Actual, Is.EqualTo("abc"));
            Assert.That(mismatch.Reason, Does.Contain("Expected a four character string"));
        });
    }

    [Test]
    public void GreaterThanOrEqualTo_ShouldAcceptTheBoundaryAndRejectSmallerValues()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":10}""", new { value = JsonValue.GreaterThanOrEqualTo(10) }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":11}""", new { value = JsonValue.GreaterThanOrEqualTo(10) }));

        var mismatch = Mismatch(JsonValue.GreaterThanOrEqualTo(10), """{"value":9}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("greater than or equal to 10"));
            Assert.That(mismatch.Actual, Is.EqualTo(9L));
        });
    }

    [Test]
    public void LessThanOrEqualTo_ShouldAcceptTheBoundaryAndRejectLargerValues()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":10}""", new { value = JsonValue.LessThanOrEqualTo(10) }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":9}""", new { value = JsonValue.LessThanOrEqualTo(10) }));

        var mismatch = Mismatch(JsonValue.LessThanOrEqualTo(10), """{"value":11}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("less than or equal to 10"));
            Assert.That(mismatch.Actual, Is.EqualTo(11L));
        });
    }

    [Test]
    public void OneOf_ShouldAcceptAnyListedValue()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":"pending"}""", new { value = JsonValue.OneOf("active", "pending") }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":2}""", new { value = JsonValue.OneOf(1, 2, 3) }));

        var mismatch = Mismatch(JsonValue.OneOf("active", "pending"), """{"value":"closed"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("one of [active, pending]"));
            Assert.That(mismatch.Actual, Is.EqualTo("closed"));
        });
    }

    [Test]
    public void OneOf_ShouldRequireATypeCompatibleValue()
    {
        // The string "2" must not satisfy a numeric constraint, and 2 must not satisfy a string one.
        var stringAgainstNumber = Mismatch(JsonValue.OneOf(1, 2, 3), """{"value":"2"}""");
        var numberAgainstString = Mismatch(JsonValue.OneOf("1", "2"), """{"value":2}""");
        var nullAgainstString = Mismatch(JsonValue.OneOf("a", "b"), """{"value":null}""");

        Assert.Multiple(() =>
        {
            Assert.That(stringAgainstNumber.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(numberAgainstString.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(nullAgainstString.PropertyPath, Is.EqualTo("$.value"));
        });

        // Numeric widening stays allowed because JSON numbers surface as decimal.
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":2}""", new { value = JsonValue.OneOf(1, 2, 3) }));
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":2.0}""", new { value = JsonValue.OneOf(2) }));
    }

    [Test]
    public void Matching_ShouldApplyThePredicateToTheRawValue()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":101}""",
            new { value = JsonValue.Matching(value => value is decimal number && number > 100, "a number greater than 100") }));

        // Objects and arrays reach the predicate as raw JSON text.
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":{"id":1}}""",
            new { value = JsonValue.Matching(value => value is string text && text.StartsWith('{'), "an object") }));

        var mismatch = Mismatch(
            JsonValue.Matching(value => value is decimal number && number > 100, "a number greater than 100"),
            """{"value":50}""");

        Assert.Multiple(() =>
        {
            Assert.That(mismatch.PropertyPath, Is.EqualTo("$.value"));
            Assert.That(mismatch.Expected, Is.EqualTo("a number greater than 100"));
            Assert.That(mismatch.Actual, Is.EqualTo(50L));
        });
    }

    [Test]
    public void Any_ShouldAcceptNumbersOutsideTheDecimalRange()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch("""{"value":1e100}""", new { value = JsonValue.Any() }));
    }

    [Test]
    public void LessThan_ShouldCompareNumbersOutsideTheDecimalRange()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":1e100}""", new { value = JsonValue.LessThan(1e101) }));
    }

    [Test]
    public void ExpectedDoubleMaxValue_ShouldMatchWithoutThrowing()
    {
        Assert.DoesNotThrow(() => JsonShapeMatcher.AssertMatch(
            """{"value":1.7976931348623157e308}""", new { value = double.MaxValue }));

        // The expectation is out of decimal range: the comparison must fall back instead of throwing.
        var mismatch = Assert.Throws<JsonShapeMismatchException>(() =>
            JsonShapeMatcher.AssertMatch("""{"value":1}""", new { value = double.MaxValue }));
        Assert.That(mismatch!.Mismatches.Single().PropertyPath, Is.EqualTo("$.value"));
    }

    private static JsonShapeMismatch Mismatch(IJsonValueMatcher matcher, string json)
    {
        var exception = Assert.Throws<JsonShapeMismatchException>(
            () => JsonShapeMatcher.AssertMatch(json, new { value = matcher }));
        return exception!.Mismatches.Single();
    }
}
