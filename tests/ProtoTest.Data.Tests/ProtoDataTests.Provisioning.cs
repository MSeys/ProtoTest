namespace ProtoTest.Data.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public sealed partial class ProtoDataTests
{
    [Test]
    public async Task Build_ShouldNotGuessNumericBusinessValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("numeric data", TestMethods.Placeholder);

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
        await host.StartTestAsync("custom resolver", TestMethods.Placeholder);

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
        await host.StartTestAsync("provision data", TestMethods.Placeholder);

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
        await host.StartTestAsync("provision many data", TestMethods.Placeholder);

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
            Assert.That(
                group.Attributes["data.type"],
                Is.EqualTo(typeof(StoredInvoice).FullName),
                "the batch records the input type under the same key every other data operation uses");
            Assert.That(group.Attributes["data.result_type"], Is.EqualTo(typeof(StoredInvoice).FullName));
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

    [Test]
    public async Task AddData_ShouldRegisterOneCapabilityWhenCalledTwice()
    {
        var probe = new CapabilityProbe();
        await using var host = new ProtoHostBuilder()
            .AddData()
            .AddData()
            .ConfigureServices(services => services.AddSingleton(probe))
            .AddTestHook<CapabilityCountingHook>()
            .Build();

        Assert.That(probe.DataCapabilityCount, Is.EqualTo(1));
        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public void Registry_ShouldTolerateConcurrentMutationsAndReads()
    {
        var registry = new ProtoTest.Data.Internal.ProtoDataRegistry();
        var writerTypes = new[] { typeof(int), typeof(string), typeof(long), typeof(Guid) };
        using var start = new ManualResetEventSlim(initialState: false);
        var tasks = new List<Task>();
        for (var writer = 0; writer < writerTypes.Length; writer++)
        {
            var valueType = typeof(List<>).MakeGenericType(writerTypes[writer]);
            tasks.Add(Task.Run(() =>
            {
                start.Wait();
                for (var round = 0; round < 200; round++)
                {
                    registry.RedactValueType(valueType);
                    registry.AddResolver(new NoopResolver());
                }
            }));
        }

        for (var reader = 0; reader < 4; reader++)
        {
            tasks.Add(Task.Run(() =>
            {
                start.Wait();
                for (var round = 0; round < 200; round++)
                {
                    _ = registry.Resolvers.Count;
                    _ = registry.IsRedacted(typeof(SecretHolder), nameof(SecretHolder.Payload), typeof(object));
                    _ = registry.TryGetFactory(typeof(SecretHolder), out _);
                }
            }));
        }

        start.Set();
        Assert.DoesNotThrow(() => Task.WaitAll([.. tasks]));
        Assert.Multiple(() =>
        {
            Assert.That(registry.Resolvers, Has.Count.EqualTo(800));
            Assert.That(registry.IsRedacted(typeof(SecretHolder), nameof(SecretHolder.Payload), typeof(List<int>)), Is.True);
        });
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
        await host.StartTestAsync("owned data", TestMethods.Placeholder);

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
        await host.StartTestAsync("cleanup order", TestMethods.Placeholder);

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
        await host.StartTestAsync("resolve ref", TestMethods.Placeholder);

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
        await host.StartTestAsync("resolve ref by identity", TestMethods.Placeholder);

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
    public async Task Ref_ShouldNameTheSharedIdentityWhenSeveralValuesUseIt()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .AddDataProvisioner<StoredInvoice, SharedIdentityProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("shared identity ref", TestMethods.Placeholder);

        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-1").CreateAsync();
        await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-2").CreateAsync();

        var exception = Assert.Throws<ProtoDataException>(
            () => Proto.Context.Data().Ref<StoredInvoice>("shared"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("shared"));
            Assert.That(exception.Message, Does.Contain("StoredInvoice"));
        });
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
        await host.StartTestAsync("missing ref", TestMethods.Placeholder);

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
        await host.StartTestAsync("reference data", TestMethods.Placeholder);

        var stored = await Proto.Context.Data().For<StoredInvoice>().With(x => x.Reference, "INV-9").CreateAsync();
        var invoice = Proto.Context.Data().For<Invoice>().With(x => x.Total, 125m).Build();

        Assert.That(invoice.Note, Is.EqualTo(stored.Reference));
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    public sealed record StoredInvoice(Guid Id, string Reference);
}
