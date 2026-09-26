namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Text;
using System.Text.Json;
using ProtoTest.Http;

/// <summary>
/// A GraphQL response built without an execution context (an untraced assertion) must behave like
/// REST's: assertions and reads still work and still name the operation, they just record nothing.
/// Audit 5 A5-38 (B-13).
/// </summary>
[TestFixture]
public sealed class GraphQLUntracedResponseTests
{
    [Test]
    public void Assertions_ShouldRunUntracedInsteadOfThrowingNullReference()
    {
        using var response = Create("""{"data":{"value":42}}""");

        Assert.DoesNotThrow(() => response.Should.HaveNoErrors());
        var mismatch = Assert.Throws<GraphQLAssertionException>(
            () => response.Should.MatchShape(new { value = 1 }));

        Assert.That(mismatch!.Message, Does.StartWith("query <anonymous> — Shape mismatch"),
            "the failure still names the operation");
    }

    [Test]
    public void Reads_ShouldWorkUntracedAndStillNameTheOperation()
    {
        using var response = Create("""{"data":{"value":42}}""");

        var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadDataAs<int>("$.missing"));

        Assert.Multiple(() =>
        {
            Assert.That(response.ReadDataAs<ValueData>()!.Value, Is.EqualTo(42));
            Assert.That(response.ReadRequired<ValueData>().Value, Is.EqualTo(42));
            Assert.That(exception!.Message, Does.StartWith(
                "query <anonymous> — The JSON path '$.missing' did not match"));
        });
    }

    [Test]
    public void ErrorsOnlyResponse_ShouldAssertUntracedAndNameTheServerError()
    {
        using var response = Create("""{"errors":[{"message":"boom"}]}""");

        Assert.DoesNotThrow(() => response.Should.HaveErrors());
        var mismatch = Assert.Throws<GraphQLAssertionException>(
            () => response.Should.MatchShape(new { value = 1 }));

        Assert.That(mismatch!.Message, Does.Contain("Server errors: boom"));
    }

    private static GraphQLResponse Create(string json)
    {
        var operation = new GraphQLBuiltOperation("query <anonymous> { value }", GraphQLOperationKind.Query, null);
        return new GraphQLResponse(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            json,
            TimeSpan.Zero,
            new ProtoHttpResponseContext(Identifier: "query <anonymous>"),
            operation);
    }

    private sealed record ValueData(int Value);
}
