namespace ProtoTest.Grpc.Tests;

using System.Reflection;
using global::Grpc.Core;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;
using ProtoTest.Json;

[TestFixture]
public sealed class ProtoGrpcAssertionsTests
{
    [Test]
    public async Task For_Should_MatchShape_ShouldRecordTheAssertionAndObservation()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc traced shape", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "hello" });

        Assert.That(
            context.RecordedObservations.Any(observation => observation.Kind == "grpc.contract.shape"),
            Is.True);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(operation.Source, Is.EqualTo("ProtoTest.Grpc"));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("matched"));
            Assert.That(operation.Attributes["shape.expected"], Does.Contain("hello"));
            Assert.That(operation.Attributes["shape.actual"], Does.Contain("hello"));
            Assert.That(operation.Attributes["shape.matches"], Does.Contain("$.message"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_ShouldNameTheMessageTypeOnMismatch()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc traced shape mismatch", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        var exception = Assert.Throws<GrpcAssertionException>(
            () => ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "goodbye" }));
        var mismatch = exception!.InnerException as JsonShapeMismatchException;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception.Message, Does.StartWith(
                $"{typeof(EchoReply).FullName} - Shape mismatch failed with 1 error(s):"));
            Assert.That(mismatch, Is.Not.Null, "the shared mismatch data stays reachable");
            Assert.That(mismatch!.Mismatches, Has.Count.EqualTo(1));
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Attributes["shape.mismatch_count"], Is.EqualTo("1"));
            Assert.That(operation.Attributes["shape.mismatches"], Does.Contain("Values did not match"));
            Assert.That(operation.Error, Is.Not.Null);
        });
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_ShouldReturnTheMessageSoAssertionsChain()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc shape chain", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        var returned = ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "hello" });

        Assert.That(returned, Is.SameAs(reply));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_Exact_ShouldRejectFieldsTheShapeDoesNotMention()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc exact shape", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "hello" });

        var exception = Assert.Throws<GrpcAssertionException>(
            () => ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "hello" }, exact: true));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("$.password"));
            Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch data stays reachable");
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_Exact_ShouldPassWhenEveryFieldIsMentioned()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc exact success", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello", Password = "secret" };

        var returned = ProtoGrpcAssertions.For(reply).Should.MatchShape(
            new { message = "hello", password = "secret" }, exact: true);

        Assert.That(returned, Is.SameAs(reply));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_Exact_ShouldTreatAValueConstraintAsMentioningItsSubtree()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc exact constraint", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello", Password = "secret" };

        ProtoGrpcAssertions.For(reply).Should.MatchShape(
            new { message = "hello", password = JsonValue.NotNull() }, exact: true);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_MatchShape_Exact_ShouldKeepIgnoringExtraFieldsWithoutExact()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc partial shape", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        Assert.DoesNotThrow(() => ProtoGrpcAssertions.For(reply).Should.MatchShape(new { message = "hello" }));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Obsolete_ShouldMatchShape_ShouldStillDelegateToTheFacade()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc obsolete shape", TestMethods.Placeholder);
        var reply = new EchoReply { Message = "hello" };

        // Intentional: pins the obsolete shim while it delegates to the facade; CS0618 is expected.
#pragma warning disable CS0618
        var returned = reply.ShouldMatchShape(new { message = "hello" }).ShouldMatchShape(new { message = "hello" });
        var failure = Assert.Throws<GrpcAssertionException>(
            () => reply.ShouldMatchShape(new { message = "other" }));
#pragma warning restore CS0618

        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(reply));
            Assert.That(failure!.Message, Does.Contain("$.message"));
        }

        await host.CompleteTestAsync(ProtoTestResult.Failed(failure!));
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries
            .Count(entry => entry.Kind == "assert.json.shape"), Is.EqualTo(3),
            "the obsolete extension still records its own assertion");
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_HaveStatus_ShouldRecordTheAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc status", TestMethods.Placeholder);
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var returned = ProtoGrpcAssertions.For(exception).Should.HaveStatus(StatusCode.NotFound);

        Assert.That(returned, Is.SameAs(exception));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.grpc.status");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(operation.Source, Is.EqualTo("ProtoTest.Grpc"));
            Assert.That(operation.Attributes["expected.rpc.grpc.status_code"], Is.EqualTo("5"));
            Assert.That(operation.Attributes["actual.rpc.grpc.status_code"], Is.EqualTo("5"));
            Assert.That(operation.Attributes["actual.rpc.grpc.status"], Is.EqualTo("NotFound"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Success));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task For_Should_HaveStatus_ShouldThrowGrpcAssertionExceptionOnMismatch()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc status mismatch", TestMethods.Placeholder);
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var thrown = Assert.Throws<GrpcAssertionException>(
            () => ProtoGrpcAssertions.For(exception).Should.HaveStatus(StatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain("NotFound"));
            Assert.That(thrown.Message, Does.Contain("OK"));
        });
        await host.CompleteTestAsync(ProtoTestResult.Failed(thrown!));
        await host.StopAsync();
    }

    [Test]
    public async Task For_ShouldNot_HaveStatus_ShouldRecordThePassingNegatedAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc negated status", TestMethods.Placeholder);
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var returned = ProtoGrpcAssertions.For(exception).ShouldNot.HaveStatus(StatusCode.OK);

        Assert.That(returned, Is.SameAs(exception));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.grpc.status");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(operation.Attributes["expected.rpc.grpc.status_code"], Is.EqualTo("0"));
            Assert.That(operation.Attributes["actual.rpc.grpc.status_code"], Is.EqualTo("5"));
            Assert.That(operation.Attributes["assertion.negated"], Is.EqualTo("true"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Success));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task For_ShouldNot_HaveStatus_ShouldRecordTheFailedNegatedAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc negated status fail", TestMethods.Placeholder);
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var thrown = Assert.Throws<GrpcAssertionException>(
            () => ProtoGrpcAssertions.For(exception).ShouldNot.HaveStatus(StatusCode.NotFound));

        Assert.That(thrown!.Message, Does.Contain("Expected gRPC status not 5 (NotFound), but received 5 (NotFound)"));
        await host.CompleteTestAsync(ProtoTestResult.Failed(thrown));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.grpc.status");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["expected.rpc.grpc.status_code"], Is.EqualTo("5"));
            Assert.That(operation.Attributes["actual.rpc.grpc.status_code"], Is.EqualTo("5"));
            Assert.That(operation.Attributes["assertion.negated"], Is.EqualTo("true"));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
            Assert.That(operation.Sections![0].Items![0].Detail, Does.Contain("not"));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task Obsolete_StatusExtensions_ShouldStillDelegateToTheFacade()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("grpc obsolete status", TestMethods.Placeholder);
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        // Intentional: pins the obsolete shims while they delegate to the facade; CS0618 is expected.
#pragma warning disable CS0618
        var positive = exception.ShouldHaveStatus(StatusCode.NotFound);
        var negated = exception.ShouldNotHaveStatus(StatusCode.OK);
        var thrown = Assert.Throws<GrpcAssertionException>(
            () => exception.ShouldHaveStatus(StatusCode.OK));
#pragma warning restore CS0618

        using (Assert.EnterMultipleScope())
        {
            Assert.That(positive, Is.SameAs(exception));
            Assert.That(negated, Is.SameAs(exception));
            Assert.That(thrown!.Message, Does.Contain("Expected gRPC status 0 (OK), but received 5 (NotFound)"));
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries
            .Count(entry => entry.Kind == "assert.grpc.status"), Is.EqualTo(3),
            "each obsolete extension still records its own assertion");
        await host.StopAsync();
    }


}
