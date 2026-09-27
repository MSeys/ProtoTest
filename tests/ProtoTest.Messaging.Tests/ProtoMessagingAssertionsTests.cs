namespace ProtoTest.Messaging.Tests;

using System.Reflection;
using ProtoTest.Core;
using ProtoTest.Json;

[TestFixture]
public sealed class ProtoMessagingAssertionsTests
{
    [Test]
    public async Task MatchShape_ShouldRecordTheAssertionAndTheObservation()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("messaging shape", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42,"status":"paid"}""", ContentType: "application/json");

        var returned = message.Should.MatchShape(new { id = 42, status = "paid" });

        Assert.That(returned, Is.SameAs(message), "Should.MatchShape returns the message, so assertions chain");
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
    public async Task MatchShape_ShouldNameTheDestinationOnMismatch()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging shape mismatch", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42}""");

        var exception = Assert.Throws<MessagingAssertionException>(
            () => message.Should.MatchShape(new { id = 7 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.StartWith(
                "invoice.paid — Shape mismatch failed with 1 error(s):"));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch data stays reachable");
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Attributes["shape.mismatch_count"], Is.EqualTo("1"));
            Assert.That(operation.Error, Is.Not.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_ShouldNameTheDestinationForEmptyOrNonJsonPayloads()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging payload", TestMethods.Placeholder);

        var empty = Assert.Throws<MessagingAssertionException>(
            () => new ProtoMessage("invoice.paid", null).Should.MatchShape(new { id = 42 }));
        var text = Assert.Throws<MessagingAssertionException>(
            () => new ProtoMessage("invoice.paid", "not json").Should.MatchShape(new { id = 42 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(text!));
        Assert.Multiple(() =>
        {
            Assert.That(empty!.Message, Does.StartWith("invoice.paid — "));
            Assert.That(empty.Message, Does.Contain("Expected JSON"));
            Assert.That(empty.InnerException, Is.TypeOf<JsonDocumentAssertionException>());
            Assert.That(text!.Message, Does.StartWith("invoice.paid — "));
            Assert.That(text.Message, Does.Contain("Expected valid JSON"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries
                .Count(entry => entry.Kind == "assert.json.shape"), Is.EqualTo(2));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_Exact_ShouldRejectFieldsTheShapeDoesNotMention()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging exact shape", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42,"extra":true}""");

        message.Should.MatchShape(new { id = 42 });

        var exception = Assert.Throws<MessagingAssertionException>(
            () => message.Should.MatchShape(new { id = 42 }, exact: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith(
                "invoice.paid — Shape mismatch failed with 1 error(s):"));
            Assert.That(exception.Message, Does.Contain("$.extra"));
            Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch data stays reachable");
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_Exact_ShouldPassWhenEveryFieldIsMentioned()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging exact success", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42,"status":"paid"}""");

        var returned = message.Should.MatchShape(new { id = 42, status = "paid" }, exact: true);

        Assert.That(returned, Is.SameAs(message), "Should.MatchShape returns the message, so assertions chain");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_Exact_ShouldTreatAValueConstraintAsMentioningItsSubtree()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging exact constraint", TestMethods.Placeholder);
        var message = new ProtoMessage(
            "invoice.paid",
            """{"id":42,"customer":{"email":"ada@example.test","phone":"555"}}""");

        message.Should.MatchShape(new { id = 42, customer = JsonValue.NotNull() }, exact: true);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task MatchShape_Exact_ShouldKeepIgnoringExtraFieldsWithoutExact()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging partial shape", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42,"extra":true}""");

        Assert.DoesNotThrow(() => message.Should.MatchShape(new { id = 42 }));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Obsolete_ShouldMatchShape_ShouldStillDelegateToTheFacade()
    {
        var builder = new ProtoHostBuilder();
        builder.AddMessaging();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("messaging obsolete shape", TestMethods.Placeholder);
        var message = new ProtoMessage("invoice.paid", """{"id":42,"status":"paid"}""");

        // Intentional: pins the obsolete shim while it delegates to the facade; CS0618 is expected.
#pragma warning disable CS0618
        var returned = message.ShouldMatchShape(new { id = 42 });
        var exception = Assert.Throws<MessagingAssertionException>(
            () => message.ShouldMatchShape(new { id = 7 }));
#pragma warning restore CS0618

        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(message));
            Assert.That(exception!.Message, Does.StartWith("invoice.paid — "));
        }
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();
    }
}
