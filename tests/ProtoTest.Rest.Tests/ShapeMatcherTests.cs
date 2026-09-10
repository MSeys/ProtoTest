namespace ProtoTest.Rest.Tests;

using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Matching;

[TestFixture]
public class ShapeMatcherTests
{
    [Test]
    public void AssertMatch_Should_Pass_And_Return_Matched_Properties_When_Shape_Matches()
    {
        var json = """
        {
            "id": 101,
            "name": "Matthias",
            "isActive": true,
            "address": {
                "city": "Kortrijk"
            }
        }
        """;

        var expectedShape = new
        {
            id = 101,
            name = "Matthias",
            isActive = true,
            address = new
            {
                city = "Kortrijk"
            }
        };

        IReadOnlyList<string> matchedProperties = null!;
        Assert.DoesNotThrow(() => matchedProperties = ShapeMatcher.AssertMatch(json, expectedShape));

        Assert.That(matchedProperties, Is.Not.Null);
        Assert.That(matchedProperties, Contains.Item("$"));
        Assert.That(matchedProperties, Contains.Item("$.id"));
        Assert.That(matchedProperties, Contains.Item("$.name"));
        Assert.That(matchedProperties, Contains.Item("$.isActive"));
        Assert.That(matchedProperties, Contains.Item("$.address"));
        Assert.That(matchedProperties, Contains.Item("$.address.city"));
    }

    [Test]
    public void AssertMatch_Should_Collect_Multiple_Mismatches_In_Single_Exception()
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

        var ex = Assert.Throws<ShapeMismatchException>(() => ShapeMatcher.AssertMatch(json, expectedShape));

        Assert.That(ex, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex, Is.InstanceOf<ProtoAssertionException>());
            Assert.That(ex!.Mismatches, Has.Count.EqualTo(3));

            Assert.That(ex.Mismatches[0].PropertyPath, Is.EqualTo("$.name"));
            Assert.That(ex.Mismatches[0].Expected, Is.EqualTo("Matthias"));
            Assert.That(ex.Mismatches[0].Actual, Is.EqualTo("Sven"));

            Assert.That(ex.Mismatches[1].PropertyPath, Is.EqualTo("$.age"));
            Assert.That(ex.Mismatches[1].Expected, Is.EqualTo(25));
            Assert.That(ex.Mismatches[1].Actual, Is.EqualTo(16L));

            Assert.That(ex.Mismatches[2].PropertyPath, Is.EqualTo("$.department"));
            Assert.That(ex.Mismatches[2].Reason, Contains.Substring("Property was missing"));

            Assert.That(ex.Message, Contains.Substring("$.name"));
        }
        Assert.That(ex.Message, Contains.Substring("$.age"));
        Assert.That(ex.Message, Contains.Substring("$.department"));
    }

    [Test]
    public void AssertMatch_Should_Support_IValueMatcher_Constraints()
    {
        var json = """
        {
            "status": "Success",
            "score": 95
        }
        """;

        var expectedShape = new
        {
            status = IsRest.NotNull(),
            score = IsRest.GreaterThan(90)
        };

        Assert.DoesNotThrow(() => ShapeMatcher.AssertMatch(json, expectedShape));
    }

    [Test]
    public void AssertMatch_Should_Fail_When_ValueMatcher_Fails()
    {
        var json = """
        {
            "score": 40
        }
        """;

        var expectedShape = new
        {
            score = IsRest.GreaterThan(50)
        };

        var ex = Assert.Throws<ShapeMismatchException>(() => ShapeMatcher.AssertMatch(json, expectedShape));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ex!.Mismatches.Count, Is.EqualTo(1));
            Assert.That(ex.Mismatches[0].PropertyPath, Is.EqualTo("$.score"));
            Assert.That(ex.Mismatches[0].Actual, Is.EqualTo(40L));
        }
    }

    [Test]
    public void AssertMatch_Should_Handle_Case_Insensitive_Property_Names()
    {
        var json = """{ "firstName": "Matthias" }""";
        var expectedShape = new { FirstName = "Matthias" };

        Assert.DoesNotThrow(() => ShapeMatcher.AssertMatch(json, expectedShape));
    }

    [Test]
    public void AssertMatch_Should_Throw_ArgumentException_On_Empty_Json()
    {
        var expectedShape = new { name = "Matthias" };

        Assert.Throws<ArgumentException>(() => ShapeMatcher.AssertMatch("", expectedShape));
        Assert.Throws<ArgumentException>(() => ShapeMatcher.AssertMatch("   ", expectedShape));
    }
}
