namespace ProtoTest.Json.Tests;

using System.Text.Json;

/// <summary>
/// Audit 5 A5.12 (B01): the shared JSON read. REST, GraphQL and messaging supply their own exception
/// type and trace vocabulary through <see cref="ProtoJsonReadSemantics"/>; the mechanics - empty body,
/// path resolution, the required null rules and the deserializer - are one implementation.
/// </summary>
[TestFixture]
public sealed class ProtoJsonReadTests
{
    private sealed class ReadFailure(string message, Exception? inner = null) : InvalidOperationException(message, inner);

    private static ProtoJsonReadSemantics Semantics(Action<Exception, string?>? trace = null) => new(
        RequiredFailure: message => new ReadFailure(message),
        PathMissFailure: (message, inner) => new ReadFailure(message, inner),
        EmptyBodyReason: "the body was empty",
        NullBodyReason: "the body was JSON null",
        TraceFailure: trace);

    [Test]
    public void Read_ShouldReturnDefaultForAnEmptyDocumentWhenNotRequired()
    {
        Assert.That(ProtoJsonRead.Read<int?>(null, null, required: false, Semantics()), Is.Null);
        Assert.That(ProtoJsonRead.Read<string>("", null, required: false, Semantics()), Is.Null);
    }

    [Test]
    public void Read_ShouldFailARequiredReadOfAnEmptyDocumentWithTheSharedName()
    {
        var exception = Assert.Throws<ReadFailure>(() =>
            ProtoJsonRead.Read<int>(null, "$.id", required: true, Semantics()));

        Assert.That(exception!.Message, Is.EqualTo("ReadRequired<Int32>('$.id') failed: the body was empty."));
    }

    [Test]
    public void Read_ShouldFailARequiredReadOfJsonNullWithTheProtocolWording()
    {
        var exception = Assert.Throws<ReadFailure>(() =>
            ProtoJsonRead.Read<object>("null", null, required: true, Semantics()));
        Assert.That(exception!.Message, Is.EqualTo("ReadRequired<Object> failed: the body was JSON null."));
    }

    [Test]
    public void Read_ShouldResolvePathsAndNameTheJsonNullValue()
    {
        const string json = """{"customer":{"id":null},"items":[1,2]}""";

        Assert.Multiple(() =>
        {
            Assert.That(ProtoJsonRead.Read<int>(json, "$.items[1]", required: true, Semantics()), Is.EqualTo(2));
            Assert.That(
                Assert.Throws<ReadFailure>(() =>
                    ProtoJsonRead.Read<int>(json, "$.customer.id", required: true, Semantics()))!.Message,
                Is.EqualTo("ReadRequired<Int32>('$.customer.id') failed: the value at '$.customer.id' was JSON null."));
        });
    }

    [Test]
    public void Read_ShouldHandEveryFailureToTheTraceSinkExactlyOnce()
    {
        var traced = new List<(Exception Failure, string? Path)>();
        var semantics = Semantics((failure, path) => traced.Add((failure, path)));

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ReadFailure>(() => ProtoJsonRead.Read<int>(null, null, required: true, semantics));
            Assert.Throws<ReadFailure>(() => ProtoJsonRead.Read<int>("""{"id":42}""", "$.missing", false, semantics));
            Assert.Throws<JsonException>(() => ProtoJsonRead.Read<int>("""{"id":"text"}""", "$.id", false, semantics));
            Assert.That(ProtoJsonRead.Read<int>("""{"id":42}""", "$.id", false, semantics), Is.EqualTo(42));
        }

        Assert.Multiple(() =>
        {
            Assert.That(traced, Has.Count.EqualTo(3), "only failures are traced");
            Assert.That(traced[0].Path, Is.Null);
            Assert.That(traced[1].Path, Is.EqualTo("$.missing"));
            Assert.That(traced[2].Failure, Is.InstanceOf<JsonException>());
        });
    }

    [Test]
    public void Read_ShouldReturnDefaultForAJsonNullRootWhenTheProtocolOptsIn()
    {
        var semantics = Semantics() with { NullRootReturnsDefault = true };

        Assert.Multiple(() =>
        {
            Assert.That(ProtoJsonRead.Read<int?>("null", "$.value", required: false, semantics), Is.Null);
            Assert.That(
                Assert.Throws<ReadFailure>(() =>
                    ProtoJsonRead.Read<int>("null", "$.value", required: true, Semantics()))!.Message,
                Does.Contain("did not match"),
                "without the opt-in a path read of a null root is a path miss");
        });
    }

    [Test]
    public void Read_ShouldDeserializeWithTheSharedReaderDefaults()
    {
        var value = ProtoJsonRead.Read<SharedDto>("""{"Name":"Ada"}""", null, required: false, Semantics());

        Assert.That(value!.Name, Is.EqualTo("Ada"), "property names match case-insensitively by default");
    }

    private sealed class SharedDto
    {
        public string? Name { get; set; }
    }
}
