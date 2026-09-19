namespace ProtoTest.Messaging.Tests;

using System.Reflection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Json;

[TestFixture]
public sealed class ProtoMessagingAssertionsTests
{
    [Test]
    public async Task ShouldMatchShape_ShouldRecordTheAssertionAndTheObservation()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging shape", TestMethod());
        var message = new ProtoMessage("invoice.paid", """{"id":42,"status":"paid"}""", ContentType: "application/json");

        message.ShouldMatchShape(context, new { id = 42, status = "paid" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(operation.Source, Is.EqualTo("ProtoTest.Messaging"));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("matched"));
            Assert.That(operation.Attributes["messaging.destination"], Is.EqualTo("invoice.paid"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            var observation = context.RecordedObservations.Single(item => item.Kind == "messaging.contract.shape");
            Assert.That(observation.Identifier, Is.EqualTo("invoice.paid"));
            Assert.That(((MessagingShapeMatchData)observation.Data!).MatchedProperties, Does.Contain("$.id"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task ShouldMatchShape_ShouldFailTheOperationAndRethrowOnMismatch()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging shape mismatch", TestMethod());
        var message = new ProtoMessage("invoice.paid", """{"id":42}""");

        var exception = Assert.Throws<JsonShapeMismatchException>(
            () => message.ShouldMatchShape(context, new { id = 7 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Attributes["shape.mismatch_count"], Is.EqualTo("1"));
            Assert.That(operation.Error, Is.Not.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task ShouldMatchShape_ShouldNameTheDestinationForEmptyOrNonJsonPayloads()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging payload", TestMethod());

        var empty = Assert.Throws<JsonDocumentAssertionException>(
            () => new ProtoMessage("invoice.paid", null).ShouldMatchShape(context, new { id = 42 }));
        var text = Assert.Throws<JsonDocumentAssertionException>(
            () => new ProtoMessage("invoice.paid", "not json").ShouldMatchShape(context, new { id = 42 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(text!));
        Assert.Multiple(() =>
        {
            Assert.That(empty!.Message, Does.Contain("invoice.paid"));
            Assert.That(empty.Message, Does.Contain("not valid Json"));
            Assert.That(text!.Message, Does.Contain("invoice.paid"));
            Assert.That(text.Message, Does.Contain("not valid Json"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries
                .Count(entry => entry.Kind == "assert.json.shape"), Is.EqualTo(2));
        });
        await host.StopAsync();
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoMessagingAssertionsTests).GetMethod(
            nameof(Placeholder),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
