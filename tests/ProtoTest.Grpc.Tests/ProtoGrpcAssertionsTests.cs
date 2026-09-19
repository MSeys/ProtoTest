namespace ProtoTest.Grpc.Tests;

using System.Reflection;
using global::Grpc.Core;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;
using ProtoTest.Json;

[TestFixture]
public sealed class ProtoGrpcAssertionsTests
{
    [Test]
    public async Task ShouldMatchShape_WithContext_ShouldRecordTheAssertionAndObservation()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc traced shape", TestMethod());
        var reply = new EchoReply { Message = "hello" };

        reply.ShouldMatchShape(context, new { message = "hello" });

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
    public async Task ShouldMatchShape_WithContext_ShouldFailTheOperationAndRethrowOnMismatch()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc traced shape mismatch", TestMethod());
        var reply = new EchoReply { Message = "hello" };

        var exception = Assert.Throws<JsonShapeMismatchException>(
            () => reply.ShouldMatchShape(context, new { message = "goodbye" }));
        Assert.That(exception!.Mismatches, Has.Count.EqualTo(1));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
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
    public async Task ShouldHaveStatus_WithContext_ShouldRecordTheAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc status", TestMethod());
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        exception.ShouldHaveStatus(StatusCode.NotFound, context);

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
    public void ShouldHaveStatus_ShouldThrowGrpcAssertionExceptionOnMismatch()
    {
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var thrown = Assert.Throws<GrpcAssertionException>(() => exception.ShouldHaveStatus(StatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(thrown!.Message, Does.Contain("NotFound"));
            Assert.That(thrown.Message, Does.Contain("OK"));
        });
    }

    [Test]
    public async Task ShouldNotHaveStatus_WithContext_ShouldRecordThePassingNegatedAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc negated status", TestMethod());
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        exception.ShouldNotHaveStatus(StatusCode.OK, context);

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
    public async Task ShouldNotHaveStatus_WithContext_ShouldRecordTheFailedNegatedAssertion()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc negated status fail", TestMethod());
        var exception = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var thrown = Assert.Throws<GrpcAssertionException>(
            () => exception.ShouldNotHaveStatus(StatusCode.NotFound, context));

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

    private static MethodInfo TestMethod()
        => typeof(ProtoGrpcAssertionsTests).GetMethod(
            nameof(Placeholder),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
