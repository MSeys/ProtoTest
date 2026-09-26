namespace ProtoTest.Json.Tests;

using System.Text.Json;

[TestFixture]
public sealed class JsonPathResolverTests
{
    private const string Document = """
    {
        "id": 42,
        "customer": { "name": "Ada", "ID": "legacy" },
        "items": [ { "sku": "A" }, { "sku": "B" } ],
        "nullable": null,
        "amount": 19.95
    }
    """;

    [TestCase("id", 42)]
    [TestCase("$.id", 42)]
    [TestCase("$.customer.name", "Ada")]
    [TestCase("$.items[0].sku", "A")]
    [TestCase("$.items[1].sku", "B")]
    public void Resolve_ShouldFollowTheDocumentedSubset(string path, object expected)
    {
        using var document = JsonDocument.Parse(Document);

        var value = JsonPathResolver.Resolve(document.RootElement, path);
        object actual = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDecimal(),
            JsonValueKind.String => value.GetString()!,
            _ => throw new AssertionException($"unexpected kind {value.ValueKind}")
        };

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void Resolve_ShouldReturnTheRootElement()
    {
        using var document = JsonDocument.Parse(Document);

        var value = JsonPathResolver.Resolve(document.RootElement, "$");

        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public void Resolve_ShouldReturnNullForAJsonNullMember()
    {
        using var document = JsonDocument.Parse(Document);

        var value = JsonPathResolver.Resolve(document.RootElement, "$.nullable");

        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Resolve_ShouldReportAMissingMemberWithThePath()
    {
        using var document = JsonDocument.Parse(Document);

        var exception = Assert.Throws<JsonPathException>(() =>
            JsonPathResolver.Resolve(document.RootElement, "$.customer.email"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Path, Is.EqualTo("$.customer.email"));
            Assert.That(exception.Reason, Does.Contain("the member 'email' was not found"));
            Assert.That(exception.Message, Is.EqualTo(
                "The JSON path '$.customer.email' did not match: the member 'email' was not found."));
        }
    }

    [Test]
    public void Resolve_ShouldNameADifferingCaseMember()
    {
        using var document = JsonDocument.Parse(Document);

        var exception = Assert.Throws<JsonPathException>(() =>
            JsonPathResolver.Resolve(document.RootElement, "$.customer.id"));

        Assert.That(exception!.Reason, Does.Contain("the document has 'ID'"));
    }

    [Test]
    public void Resolve_ShouldReportAnIndexOutsideTheArray()
    {
        using var document = JsonDocument.Parse(Document);

        var exception = Assert.Throws<JsonPathException>(() =>
            JsonPathResolver.Resolve(document.RootElement, "$.items[2].sku"));

        Assert.That(exception!.Reason, Is.EqualTo("the index [2] is outside the array's 2 element(s)."));
    }

    [TestCase("$.id.name", "the member 'name' cannot be read from a JSON number")]
    [TestCase("$.customer[0]", "the index [0] cannot be read from a JSON object")]
    [TestCase("$.items.sku", "the member 'sku' cannot be read from a JSON array")]
    [TestCase("$.items[-1]", "the index '[-1]' is not a non-negative integer")]
    [TestCase("$.items[", "a closing ']' was expected")]
    [TestCase("$.items[0]x", "a member name or index was expected, but found 'x'")]
    [TestCase("$.", "a member name was expected after '.'")]
    public void Resolve_ShouldReportWhatWentWrong(string path, string expectedReasonPart)
    {
        using var document = JsonDocument.Parse(Document);

        var exception = Assert.Throws<JsonPathException>(() =>
            JsonPathResolver.Resolve(document.RootElement, path));

        Assert.That(exception!.Reason, Does.Contain(expectedReasonPart));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Resolve_ShouldRejectABlankPath(string path)
    {
        using var document = JsonDocument.Parse(Document);

        Assert.Throws<ArgumentException>(() => JsonPathResolver.Resolve(document.RootElement, path));
    }
}
