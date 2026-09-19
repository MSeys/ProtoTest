namespace ProtoTest.Data.Tests;

using System.Reflection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoDataValueIdentityTests
{
    [Test]
    public async Task ProvisionAsync_ShouldUseTheSnakeCaseTypeSegmentForTheValueIdentity()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .AddDataProvisioner<InvoiceLine, InvoiceLineProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("snake case value identity", TestMethod());

        await Proto.Context.Data().For<InvoiceLine>()
            .With(line => line.Number, "INV-1")
            .CreateAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        var provision = test.Entries.Single(entry => entry.Kind == "data.provision");
        var value = test.Values!.Single(item => item.Kind == "value" && item.Id == "invoice_line:INV-1");
        Assert.Multiple(() =>
        {
            Assert.That(provision.Attributes["data.value_id"], Is.EqualTo("value:invoice_line:INV-1"));
            Assert.That(value.Name, Is.EqualTo("Value · InvoiceLine 'INV-1'"));
            Assert.That(value.Versions.Single().Change, Is.EqualTo("created"));
            Assert.That(
                value.Versions.Single().State["value.type"],
                Is.EqualTo(typeof(InvoiceLine).FullName));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task ProvisionAsync_ShouldUseAReadableSegmentForGenericTypes()
    {
        var builder = new ProtoHostBuilder()
            .AddData()
            .AddDataProvisioner<Envelope<InvoiceLine>, EnvelopeProvisioner>();
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StartTestAsync("generic value identity", TestMethod());

        await Proto.Context.Data().For<Envelope<InvoiceLine>>()
            .With(envelope => envelope.Value, new InvoiceLine(Guid.NewGuid(), "INV-1"))
            .CreateAsync();

        var test = host.Trace.Snapshot().Tests.Single();
        var value = test.Values!.Single(item => item.Kind == "value");
        var provision = test.Entries.Single(entry => entry.Kind == "data.provision");
        Assert.Multiple(() =>
        {
            Assert.That(value.Id, Is.EqualTo("envelope:INV-1"),
                "The generic arity must not leak into the value id (envelope`1).");
            Assert.That(provision.Attributes["data.value_id"], Is.EqualTo("value:envelope:INV-1"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoDataValueIdentityTests).GetMethod(
            nameof(Placeholder),
            BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }

    public sealed record InvoiceLine(Guid Id, string Number);

    public sealed class InvoiceLineProvisioner : IProtoDataProvisioner<InvoiceLine>
    {
        public ValueTask<ProtoDataProvisioningResult<InvoiceLine>> CreateAsync(
            InvoiceLine value,
            ProtoDataProvisioningContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ProtoDataProvisioningResult<InvoiceLine>(value, value.Number));
    }

    public sealed record Envelope<T>(T Value);

    public sealed class EnvelopeProvisioner : IProtoDataProvisioner<Envelope<InvoiceLine>>
    {
        public ValueTask<ProtoDataProvisioningResult<Envelope<InvoiceLine>>> CreateAsync(
            Envelope<InvoiceLine> value,
            ProtoDataProvisioningContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new ProtoDataProvisioningResult<Envelope<InvoiceLine>>(value, value.Value.Number));
    }
}
