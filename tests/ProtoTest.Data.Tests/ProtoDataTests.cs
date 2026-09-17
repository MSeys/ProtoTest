namespace ProtoTest.Data.Tests;

using ProtoTest.Core;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class ProtoDataTests
{
    [Test]
    public async Task Build_ShouldCombineExplicitMemberTypeAndBuiltInValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        var context = await host.StartTestAsync("build data", TestMethod());

        var invoice = Proto.Context.Data().For<Invoice>()
            .With(x => x.Total, 125m)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(invoice.Total, Is.EqualTo(125m));
            Assert.That(invoice.Currency, Is.EqualTo("EUR"));
            Assert.That(invoice.Id.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(invoice.Description, Does.StartWith("Invoice.Description-"));
            Assert.That(invoice.Lines, Is.Empty);
            Assert.That(invoice.Note, Is.Null);
        });

        var build = context.Trace is not null
            ? host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "data.build")
            : throw new AssertionException("Missing trace");
        var values = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "data.value.resolve")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(build.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(values, Has.Length.EqualTo(6));
            Assert.That(values, Has.All.Matches<ProtoTraceEntry>(entry => entry.ParentId == build.Id));
            Assert.That(values.Single(entry => entry.Attributes["data.member"] == "Total")
                .Attributes["data.source_kind"], Is.EqualTo("Explicit"));
            Assert.That(values.Single(entry => entry.Attributes["data.member"] == "Currency")
                .Attributes["data.source_kind"], Is.EqualTo("MemberDefault"));
            Assert.That(values.Single(entry => entry.Attributes["data.member"] == "Id")
                .Attributes["data.source_kind"], Is.EqualTo("TypeProvider"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Build_ShouldBindRecordPrimaryConstructor()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("record data", TestMethod());

        var command = Proto.Context.Data().For<CreateInvoice>()
            .With(x => x.Total, 40m)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(command.Id.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(command.Total, Is.EqualTo(40m));
            Assert.That(command.Reference, Does.StartWith("CreateInvoice.Reference-"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task BuiltInGeneration_ShouldBeUniqueWithinAndRepeatableAcrossTests()
    {
        await using var firstHost = CreateHost();
        await firstHost.StartAsync();
        await firstHost.StartTestAsync("deterministic data", "00042", TestMethod());
        var first = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();
        var second = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();
        await firstHost.CompleteTestAsync(ProtoTestResult.Passed);
        await firstHost.StopAsync();

        await using var secondHost = CreateHost();
        await secondHost.StartAsync();
        await secondHost.StartTestAsync("deterministic data", "00042", TestMethod());
        var repeated = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();

        Assert.Multiple(() =>
        {
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(repeated.Id, Is.EqualTo(first.Id));
            Assert.That(repeated.Reference, Is.EqualTo(first.Reference));
        });

        await secondHost.CompleteTestAsync(ProtoTestResult.Passed);
        await secondHost.StopAsync();
    }

    [Test]
    public async Task BuildMany_ShouldResolveUniqueValuesForEveryItem()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("many data", "00077", TestMethod());

        var commands = Proto.Context.Data().For<CreateInvoice>()
            .With(x => x.Total, 10m)
            .BuildMany(7);

        Assert.Multiple(() =>
        {
            Assert.That(commands, Has.Count.EqualTo(7));
            Assert.That(commands.Select(command => command.Id).Distinct().ToArray(), Has.Length.EqualTo(7));
            Assert.That(commands.Select(command => command.Reference).Distinct().ToArray(), Has.Length.EqualTo(7));
            Assert.That(commands, Has.All.Matches<CreateInvoice>(command => command.Total == 10m));
            var group = host.Trace.Snapshot().Tests.Single().Entries
                .Single(entry => entry.Kind == "data.build_many");
            Assert.That(group.Attributes["data.count"], Is.EqualTo("7"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries
                .Where(entry => entry.Kind == "data.build"),
                Has.All.Matches<ProtoTraceEntry>(entry => entry.ParentId == group.Id));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Explain_ShouldExposeProvenanceAndReuseResolvedValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("explain data", TestMethod());

        var builder = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 10m);
        var explanation = builder.Explain();
        var command = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(explanation.Values.Single(value => value.MemberName == "Id").Value,
                Is.EqualTo(command.Id));
            Assert.That(explanation.Values.Single(value => value.MemberName == "Total").SourceKind,
                Is.EqualTo("Explicit"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "data.explain"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task DomainFactory_ShouldUseNormalResolutionAndComposeAcrossAddDataCalls()
    {
        var builder = new ProtoHostBuilder()
            .AddData(data => data.Values.For<InvoiceId>()
                .Use(context => new InvoiceId(context.NextGuid())))
            .AddData(data => data.For<DomainInvoice>()
                .Default(x => x.Total, 25m)
                .ConstructUsing(context => DomainInvoice.Create(
                    context.Value<InvoiceId>(nameof(DomainInvoice.Id)),
                    context.Value<decimal>(nameof(DomainInvoice.Total)))));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("domain factory", TestMethod());

        var invoice = Proto.Context.Data().For<DomainInvoice>()
            .With(x => x.Total, 125m)
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(invoice.Total, Is.EqualTo(125m));
            Assert.That(invoice.Id.Value, Is.Not.EqualTo(Guid.Empty));
            Assert.That(context.Trace is not null
                ? host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "data.build")
                    .Attributes["data.construction_source"]
                : null,
                Is.EqualTo("Host configuration"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Trace_ShouldRedactConfiguredMemberValue()
    {
        var builder = new ProtoHostBuilder().AddData(data =>
            data.For<Credentials>().Redact(x => x.Secret));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("redacted data", TestMethod());

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
    public async Task Build_ShouldFailAndTraceUnresolvedSemanticValue()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("missing data", TestMethod());

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

    [Test]
    public async Task Build_ShouldNotGuessNumericBusinessValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("numeric data", TestMethod());

        var exception = Assert.Throws<ProtoDataException>(() => Proto.Context.Data().For<RequiresNumber>().Build());

        Assert.That(exception!.Message, Does.Contain("RequiresNumber.Amount"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task CustomResolver_ShouldExtendPipelineWithoutChangingTestArrange()
    {
        var builder = new ProtoHostBuilder().AddData(data =>
            data.AddValueResolver(new TestEmailResolver()));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("custom resolver", TestMethod());

        var contact = Proto.Context.Data().For<Contact>().Build();

        Assert.That(contact.Email.Value, Is.EqualTo("Email-0001@example.test"));
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "data.value.resolve").Attributes["data.source_kind"],
            Is.EqualTo("CustomResolver"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task CreateAsync_ShouldProvisionTraceAndCleanupOwnedData()
    {
        var cleanup = new CleanupProbe();
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(cleanup))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("provision data", TestMethod());

        var invoice = await Proto.Context.Data().For<StoredInvoice>()
            .With(x => x.Reference, "INV-42")
            .CreateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(invoice.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(invoice.Reference, Is.EqualTo("stored:INV-42"));
            Assert.That(cleanup.Disposed, Is.False);
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "data.create"));
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "data.provision"
                    && entry.Attributes["data.identity"] == invoice.Id.ToString()));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(cleanup.Disposed, Is.True);
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "data.cleanup"
                    && entry.Outcome == ProtoTraceOutcome.Succeeded));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task CreateManyAsync_ShouldGroupIndividualCreateOperations()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("provision many data", TestMethod());

        var invoices = await Proto.Context.Data().For<StoredInvoice>()
            .CreateManyAsync(3, (invoice, index) =>
                invoice.With(value => value.Reference, $"INV-{index + 1}"));

        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var group = entries.Single(entry => entry.Kind == "data.create_many");
        var creates = entries.Where(entry => entry.Kind == "data.create").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(invoices, Has.Count.EqualTo(3));
            Assert.That(group.Attributes["data.count"], Is.EqualTo("3"));
            Assert.That(creates, Has.Length.EqualTo(3));
            Assert.That(creates, Has.All.Matches<ProtoTraceEntry>(entry => entry.ParentId == group.Id));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddData_ShouldRejectAmbiguousDefaults()
    {
        var exception = Assert.Throws<ProtoDataException>(() => new ProtoHostBuilder().AddData(data =>
        {
            data.For<Invoice>().Default(x => x.Currency, "EUR");
            data.For<Invoice>().Default(x => x.Currency, "GBP");
        }));

        Assert.That(exception!.Message, Does.Contain("Ambiguous default"));
    }

    private static ProtoHost CreateHost()
        => new ProtoHostBuilder()
            .AddData(data => data.AddDefaults<TestDefaults>())
            .Build();

    private static MethodInfo TestMethod()
        => typeof(ProtoDataTests).GetMethod(nameof(TestMethod), BindingFlags.NonPublic | BindingFlags.Static)!;

    public sealed class TestDefaults : IProtoDataDefaultsModule
    {
        public void Configure(ProtoDataConfiguration data)
        {
            data.Values.For<InvoiceId>().Use(context => new InvoiceId(context.NextGuid()));
            data.For<Invoice>().Default(x => x.Currency, "EUR");
        }
    }

    public sealed class Invoice
    {
        public InvoiceId Id { get; init; }
        public string Description { get; init; } = null!;
        public string Currency { get; init; } = null!;
        public decimal Total { get; init; }
        public IReadOnlyList<string> Lines { get; init; } = null!;
        public string? Note { get; init; }
    }

    public readonly record struct InvoiceId(Guid Value);
    public sealed record CreateInvoice(InvoiceId Id, decimal Total, string Reference);
    public sealed record SemanticState(InvoiceStatus Status);
    public sealed record RequiresNumber(decimal Amount);
    public enum InvoiceStatus { Draft, Overdue }

    public sealed class DomainInvoice
    {
        private DomainInvoice(InvoiceId id, decimal total)
        {
            Id = id;
            Total = total;
        }

        public InvoiceId Id { get; }
        public decimal Total { get; }

        public static DomainInvoice Create(InvoiceId id, decimal total)
            => total <= 0
                ? throw new ArgumentOutOfRangeException(nameof(total))
                : new DomainInvoice(id, total);
    }

    public sealed class Credentials
    {
        public string UserName { get; init; } = null!;
        public string Secret { get; init; } = null!;
    }

    public sealed record Contact(EmailAddress Email);
    public sealed record EmailAddress(string Value);

    public sealed class TestEmailResolver : IProtoDataValueResolver
    {
        public bool TryResolve(ProtoDataValueContext context, out ProtoDataResolvedValue value)
        {
            if (context.ValueType == typeof(EmailAddress))
            {
                value = new ProtoDataResolvedValue(
                    new EmailAddress($"{context.MemberName}-{context.ObjectSequence:D4}@example.test"),
                    nameof(TestEmailResolver));
                return true;
            }

            value = null!;
            return false;
        }
    }

    [Test]
    public async Task CreateAsync_ShouldRegisterProvisionedDataAsOwnedResources()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("owned data", TestMethod());

        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();
        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-2").CreateAsync();

        var owned = Proto.Context.Resources.Where(resource => resource.Kind == "data").ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(owned, Has.Length.EqualTo(2));
            Assert.That(owned, Has.All.Matches<ProtoResourceSnapshot>(
                resource => resource.State == ProtoResourceState.Registered));
            Assert.That(owned, Has.All.Matches<ProtoResourceSnapshot>(
                resource => resource.Description.StartsWith("Provisioned StoredInvoice")));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var releases = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "resource.release" && entry.Attributes["resource.kind"] == "data")
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(releases, Has.Length.EqualTo(2));
            Assert.That(releases, Has.All.Matches<ProtoTraceEntry>(
                entry => entry.Outcome == ProtoTraceOutcome.Succeeded));
        });
        await host.StopAsync();
    }

    [Test]
    public async Task CreateAsync_ShouldCleanupOwnedDataBeforeClientsAreDisposed()
    {
        var probe = new CleanupProbe();
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(probe))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("cleanup order", TestMethod());

        Proto.Context.RegisterClient(new LoggingClient(probe.Log), "Tracked");
        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(probe.Log, Is.EqualTo(new[] { "data.cleanup", "client.dispose" }));
        await host.StopAsync();
    }

    [Test]
    public async Task Ref_ShouldResolveTheOnlyProvisionedInstance()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("resolve ref", TestMethod());

        var invoice = await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();
        var resolved = Proto.Context.Data().Ref<StoredInvoice>();

        Assert.That(resolved, Is.SameAs(invoice));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Ref_ShouldResolveByIdentityAndRejectAmbiguity()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("resolve ref by identity", TestMethod());

        var first = await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();
        var second = await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-2").CreateAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Proto.Context.Data().Ref<StoredInvoice>(first.Id.ToString()), Is.SameAs(first));
            Assert.That(Proto.Context.Data().Ref<StoredInvoice>(second.Id.ToString()), Is.SameAs(second));
        });

        var ambiguous = Assert.Throws<ProtoDataException>(() => Proto.Context.Data().Ref<StoredInvoice>());
        Assert.That(ambiguous!.Message, Does.Contain("identity"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Ref_ShouldExplainWhatWasProvisionedWhenNothingMatches()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("missing ref", TestMethod());

        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();

        var exception = Assert.Throws<ProtoDataException>(() => Proto.Context.Data().Ref<Invoice>());

        Assert.That(exception!.Message, Does.Contain("StoredInvoice"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Default_ShouldReferenceAnotherProvisionedObject()
    {
        var builder = new ProtoHostBuilder()
            .AddData(data =>
            {
                data.AddDefaults<TestDefaults>();
                data.For<Invoice>().Default(x => x.Note, context => (string?)context.Ref<StoredInvoice>().Reference);
            })
            .ConfigureServices(services => services.AddSingleton(new CleanupProbe()))
            .AddDataProvisioner<StoredInvoice, StoredInvoiceProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("reference data", TestMethod());

        var stored = await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-9").CreateAsync();
        var invoice = Proto.Context.Data().For<Invoice>().With(x => x.Total, 125m).Build();

        Assert.That(invoice.Note, Is.EqualTo(stored.Reference));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    public sealed record StoredInvoice(Guid Id, string Reference);

    public sealed class StoredInvoiceProvisioner : IProtoDataProvisioner<StoredInvoice>
    {
        private readonly CleanupProbe _cleanup;

        public StoredInvoiceProvisioner(CleanupProbe cleanup)
        {
            _cleanup = cleanup;
        }

        public ValueTask<ProtoDataProvisioningResult<StoredInvoice>> CreateAsync(
            StoredInvoice value,
            ProtoDataProvisioningContext context,
            CancellationToken cancellationToken)
        {
            var stored = value with { Reference = $"stored:{value.Reference}" };
            return ValueTask.FromResult(new ProtoDataProvisioningResult<StoredInvoice>(
                stored,
                stored.Id.ToString(),
                new CleanupOwnership(_cleanup)));
        }
    }

    public sealed class CleanupProbe
    {
        public bool Disposed { get; set; }

        public List<string> Log { get; } = [];
    }

    private sealed class CleanupOwnership : IAsyncDisposable
    {
        private readonly CleanupProbe _probe;

        public CleanupOwnership(CleanupProbe probe)
        {
            _probe = probe;
        }

        public ValueTask DisposeAsync()
        {
            _probe.Disposed = true;
            _probe.Log.Add("data.cleanup");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LoggingClient(List<string> log) : IDisposable
    {
        public void Dispose() => log.Add("client.dispose");
    }
}
