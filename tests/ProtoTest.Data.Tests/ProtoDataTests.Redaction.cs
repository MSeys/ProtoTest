namespace ProtoTest.Data.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public sealed partial class ProtoDataTests
{
    [Test]
    public async Task Trace_ShouldRedactConfiguredMemberValue()
    {
        var builder = new ProtoHostBuilder().AddData(data =>
            data.For<Credentials>().Redact(x => x.Secret));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted data", TestMethods.Placeholder);

        Proto.Context.Data().For<Credentials>()
            .With(x => x.Secret, "do-not-trace")
            .Build();

        var secretEntry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "data.value.resolve"
                && entry.Attributes["data.member"] == "Secret");
        Assert.Multiple(() =>
        {
            Assert.That(secretEntry.Attributes["data.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(secretEntry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(secretEntry.Attributes.Values, Has.None.EqualTo("do-not-trace"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Build_WithDateTimeDecimalAndGuidMembers_ShouldTraceThemAsValues()
    {
        // Walking a DateTime's properties never ends (its Date is another DateTime), so the trace
        // writer treats formattable value types as leaves instead of overflowing the stack.
        var at = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.Zero);
        var builder = new ProtoHostBuilder().AddData();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("dated data", TestMethods.Placeholder);

        var built = Proto.Context.Data().For<Stamped>()
            .With(x => x.At, at)
            .With(x => x.Amount, 12.5m)
            .Build();

        var entries = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "data.value.resolve")
            .ToDictionary(entry => entry.Attributes["data.member"]!, entry => entry.Attributes["data.value"]);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        Assert.Multiple(() =>
        {
            Assert.That(built.At, Is.EqualTo(at));
            Assert.That(entries[nameof(Stamped.At)], Does.Contain("2026-10-01"));
            Assert.That(entries[nameof(Stamped.Amount)], Does.Contain("12.5"));
            Assert.That(entries, Does.ContainKey(nameof(Stamped.Id)));
        });
    }

    public sealed record Stamped(DateTimeOffset At, decimal Amount, Guid Id);

    [Test]
    public async Task Trace_ShouldRedactConfiguredValueType()
    {
        var builder = new ProtoHostBuilder().AddData(data =>
        {
            data.Values.Use<Password>(_ => new Password("do-not-trace"));
            data.RedactValueType<Password>();
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted value type", TestMethods.Placeholder);

        Proto.Context.Data().For<Login>().Build();

        var passwordEntry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "data.value.resolve"
                && entry.Attributes["data.member"] == nameof(Login.Password));
        Assert.Multiple(() =>
        {
            Assert.That(passwordEntry.Attributes["data.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(passwordEntry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(passwordEntry.Attributes.Values, Has.None.EqualTo("do-not-trace"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldRedactRuntimeBaseTypeWhenDeclaredTypeIsObject()
    {
        var builder = new ProtoHostBuilder().AddData(data => data.RedactValueType<SecretBase>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted base value type", TestMethods.Placeholder);

        Proto.Context.Data().For<SecretHolder>()
            .With(x => x.Payload, new DerivedSecret { Code = "do-not-trace" })
            .Build();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "data.value.resolve"
                && item.Attributes["data.member"] == nameof(SecretHolder.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["data.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(entry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(entry.Attributes.Values, Has.None.EqualTo("do-not-trace"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldRedactRuntimeInterfaceWhenDeclaredTypeIsObject()
    {
        var builder = new ProtoHostBuilder().AddData(data => data.RedactValueType<ISecretMarker>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted interface value type", TestMethods.Placeholder);

        Proto.Context.Data().For<SecretHolder>()
            .With(x => x.Payload, new DerivedSecret { Code = "do-not-trace" })
            .Build();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "data.value.resolve"
                && item.Attributes["data.member"] == nameof(SecretHolder.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["data.value"], Is.EqualTo("[REDACTED]"));
            Assert.That(entry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(entry.Attributes.Values, Has.None.EqualTo("do-not-trace"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldRedactARedactedValueInsideACollection()
    {
        var builder = new ProtoHostBuilder().AddData(data => data.RedactValueType<Password>());
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted collection value", TestMethods.Placeholder);

        Proto.Context.Data().For<SecretHolder>()
            .With(x => x.Payload, new List<object> { new Password("do-not-trace"), "visible" })
            .Build();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "data.value.resolve"
                && item.Attributes["data.member"] == nameof(SecretHolder.Payload));
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(entry.Attributes["data.value"], Does.Contain("[REDACTED]"));
            Assert.That(entry.Attributes["data.value"], Does.Not.Contain("do-not-trace"));
            Assert.That(entry.Attributes["data.value"], Does.Contain("visible"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldRedactARedactedMemberInsideANestedObject()
    {
        var builder = new ProtoHostBuilder().AddData(data => data.For<Credentials>().Redact(x => x.Secret));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted nested member", TestMethods.Placeholder);

        Proto.Context.Data().For<CredentialHolder>()
            .With(x => x.Credentials, new Credentials { UserName = "ada", Secret = "do-not-trace" })
            .Build();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "data.value.resolve"
                && item.Attributes["data.member"] == nameof(CredentialHolder.Credentials));
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(entry.Attributes["data.value"], Does.Contain("[REDACTED]"));
            Assert.That(entry.Attributes["data.value"], Does.Not.Contain("do-not-trace"));
            Assert.That(entry.Attributes["data.value"], Does.Contain("ada"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldWalkCyclicGraphsWithoutRecursingForever()
    {
        var builder = new ProtoHostBuilder().AddData(data => data.For<CyclicNode>().Redact(x => x.Secret));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("cyclic value", TestMethods.Placeholder);

        var node = new CyclicNode { Secret = "do-not-trace" };
        node.Next = node;
        Proto.Context.Data().For<CyclicHolder>().With(x => x.Node, node).Build();

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "data.value.resolve"
                && item.Attributes["data.member"] == nameof(CyclicHolder.Node));
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["data.redacted"], Is.EqualTo("true"));
            Assert.That(entry.Attributes["data.value"], Does.Contain("[REDACTED]"));
            Assert.That(entry.Attributes["data.value"], Does.Not.Contain("do-not-trace"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task MemberDefaultsAndRedaction_ShouldApplyToDerivedTypes()
    {
        var builder = new ProtoHostBuilder().AddData(data =>
        {
            data.For<CredentialBase>().Default(x => x.UserName, "base-user");
            data.For<CredentialBase>().Redact(x => x.Secret);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("base member defaults", TestMethods.Placeholder);

        Proto.Context.Data().For<DerivedCredentials>()
            .With(x => x.Secret, "do-not-trace")
            .Build();

        var entries = host.Trace.Snapshot().Tests.Single().Entries
            .Where(item => item.Kind == "data.value.resolve")
            .ToDictionary(item => item.Attributes["data.member"]!);
        Assert.Multiple(() =>
        {
            Assert.That(entries["UserName"].Attributes["data.value"], Does.Contain("base-user"),
                "A default registered for the base type must supply the derived type's inherited member.");
            Assert.That(entries["Secret"].Attributes["data.value"], Is.EqualTo("[REDACTED]"),
                "A redaction registered for the base type must hide the derived type's inherited member.");
            Assert.That(entries["Secret"].Attributes["data.redacted"], Is.EqualTo("true"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Build_ShouldFailAndTraceUnresolvedSemanticValue()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("missing data", TestMethods.Placeholder);

        var exception = Assert.Throws<ProtoDataException>(() => Proto.Context.Data().For<SemanticState>().Build());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("SemanticState.Status"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries
                .Single(entry => entry.Kind == "data.build").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries
                .Single(entry => entry.Kind == "data.value.resolve").Attributes["data.source_kind"],
                Is.EqualTo("Unresolved"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

}
