namespace ProtoTest.Json.Tests;

using System.Reflection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoShapeAssertionTests
{
    [Test]
    public async Task Assert_ShouldRecordMatchedEvidenceAndTheObservation()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("shape assertion", TestMethod());

        var matched = ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(
                context,
                "ProtoTest.Tests",
                "Assert shape",
                CaptureExpectedShape: true,
                AttachmentName: "expected-shape"),
            """{"id":42,"name":"ProtoTest"}""",
            new { id = 42 },
            observation: properties => new ProtoObservation("Target", "test.contract.shape", "GET /test", properties));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(matched, Does.Contain("$.id"));
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(operation.Source, Is.EqualTo("ProtoTest.Tests"));
            Assert.That(operation.Attributes["expected.type"], Does.Contain("AnonymousType"));
            Assert.That(operation.Attributes["shape.expected"], Does.Contain("\"id\""));
            Assert.That(operation.Attributes["shape.actual"], Does.Contain("\"name\""));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("matched"));
            Assert.That(operation.Attributes["shape.matches"], Does.Contain("$.id"));
            Assert.That(operation.Attributes["matched.property_count"], Is.EqualTo("2"));
            Assert.That(operation.Sections!, Has.Count.EqualTo(1));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Value, Is.EqualTo("2 properties"));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Success));
            Assert.That(context.Attachments.Select(item => item.Name), Has.Some.EndsWith("expected-shape"));
            Assert.That(context.RecordedObservations.Single().Kind, Is.EqualTo("test.contract.shape"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldRecordEveryMismatchAndRethrow()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("shape mismatch", TestMethod());

        var exception = Assert.Throws<JsonShapeMismatchException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert shape"),
            """{"id":42}""",
            new { id = 43, name = "ProtoTest" }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Mismatches, Has.Count.EqualTo(2));
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Attributes["shape.mismatch_count"], Is.EqualTo("2"));
            Assert.That(operation.Attributes["shape.mismatches"], Does.Contain("Values did not match"));
            Assert.That(operation.Attributes["shape.mismatches"], Does.Contain("Property was missing"));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
            Assert.That(operation.Error, Is.Not.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldDescribeDictionaryShapesAsObjects()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("dictionary shape", TestMethod());

        var matched = ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert shape"),
            """{"account":{"city":"Ghent"}}""",
            new Dictionary<string, object?>
            {
                ["account"] = new Dictionary<string, object?> { ["city"] = "Ghent" }
            });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(matched, Does.Contain("$.account.city"));
            Assert.That(operation.Attributes["shape.expected"], Does.Contain("\"account\":{\"city\":\"Ghent\"}"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldCapTheDescriptionOfDeepAndCyclicShapes()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("deep shape", TestMethod());

        var deep = new Dictionary<string, object?>();
        var cursor = deep;
        for (var index = 0; index < 64; index++)
        {
            var next = new Dictionary<string, object?>();
            cursor["next"] = next;
            cursor = next;
        }

        cursor["value"] = 1;
        var cyclic = new NodeShape { Name = "root" };
        cyclic.Next = cyclic;

        var deepException = Assert.Throws<JsonShapeMismatchException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert deep shape"), "{}", deep));
        var cyclicException = Assert.Throws<JsonShapeMismatchException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert cyclic shape"),
            """{"name":"root"}""",
            cyclic));

        await host.CompleteTestAsync(ProtoTestResult.Failed(deepException!));
        var operations = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "assert.json.shape").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(deepException!.Mismatches, Is.Not.Empty);
            Assert.That(cyclicException!.Mismatches, Is.Not.Empty);
            Assert.That(operations, Has.Length.EqualTo(2));
            Assert.That(
                operations.All(operation =>
                    operation.Attributes["shape.expected"]?.Contains("depth limit", StringComparison.Ordinal) == true),
                Is.True);
            Assert.That(operations.Select(operation => operation.Outcome),
                Is.All.EqualTo(ProtoTraceOutcome.Failed));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldMatchAndRecordANullExpectedShape()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("null shape", TestMethod());

        var matched = ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert null shape"), "null", null);

        var mismatch = Assert.Throws<JsonShapeMismatchException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert null mismatch"),
            """{"id":1}""",
            null));

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        var operations = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "assert.json.shape").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(matched, Does.Contain("$"));
            Assert.That(mismatch!.Mismatches.Single().Reason, Is.EqualTo("Expected null."));
            Assert.That(operations[0].Attributes["shape.expected"], Is.EqualTo("null"));
            Assert.That(operations[0].Attributes["shape.result"], Is.EqualTo("matched"));
            Assert.That(operations[1].Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_WithNullActualJson_ShouldRecordTheFailureInsideTheOperation()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("null actual", TestMethod());

        var exception = Assert.Throws<JsonDocumentAssertionException>(() => ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert shape"), null, new { id = 1 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Error, Is.Not.Null);
            Assert.That(operation.Attributes.ContainsKey("shape.actual"), Is.True);
            Assert.That(operation.Attributes["shape.actual"], Is.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldSanitizeTheRecordedActualJson()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("sanitized actual", TestMethod());

        ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert shape"),
            """{"id":42,"password":"hunter2"}""",
            new { id = 42 });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Attributes["shape.actual"], Does.Not.Contain("hunter2"));
            Assert.That(operation.Attributes["shape.actual"], Does.Contain("[REDACTED]"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Assert_ShouldDescribeAndMatchNonStringKeyedDictionaryShapes()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("non-string dictionary shape", TestMethod());

        var matched = ProtoShapeAssertion.Assert(
            new ProtoShapeAssertionContext(context, "ProtoTest.Tests", "Assert shape"),
            """{"1":"one","2":"two"}""",
            new Dictionary<int, object?> { [1] = "one", [2] = "two" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(matched, Does.Contain("$.1"));
            Assert.That(operation.Attributes["shape.expected"], Does.Contain("\"1\":\"one\""));
        });
        await host.StopAsync();
    }

    private sealed class NodeShape
    {
        public string Name { get; set; } = string.Empty;
        public NodeShape? Next { get; set; }
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoShapeAssertionTests).GetMethod(
            nameof(Placeholder),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
