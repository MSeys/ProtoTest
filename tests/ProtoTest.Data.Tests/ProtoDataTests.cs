namespace ProtoTest.Data.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed partial class ProtoDataTests
{
    [Test]
    public async Task Build_ShouldCombineExplicitMemberTypeAndBuiltInValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        var context = await host.StartTestAsync("build data", TestMethods.Placeholder);

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
        await host.StartTestAsync("record data", TestMethods.Placeholder);

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
        await firstHost.StartTestAsync("deterministic data", "00042", TestMethods.Placeholder);
        var first = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();
        var second = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();
        await firstHost.CompleteTestAsync(ProtoTestResult.Passed);
        await firstHost.StopAsync();

        await using var secondHost = CreateHost();
        await secondHost.StartAsync();
        await secondHost.StartTestAsync("deterministic data", "00042", TestMethods.Placeholder);
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
        await host.StartTestAsync("many data", "00077", TestMethods.Placeholder);

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
    public async Task Build_ShouldKeepOptionalConstructorParameterDefaults()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("constructor defaults", TestMethods.Placeholder);

        var builder = Proto.Context.Data().For<OptionalDefaults>();
        var explanation = builder.Explain();
        var defaults = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Currency, Is.EqualTo("EUR"),
                "the declared string default wins over the generated string");
            Assert.That(defaults.Retries, Is.EqualTo(3));
            Assert.That(defaults.Tags, Is.Null, "the declared collection default wins over the generated empty list");
            Assert.That(defaults.Version, Is.EqualTo(Guid.Empty), "the declared struct default wins over the generated Guid");
            Assert.That(explanation.Values.Single(value => value.MemberName == "Currency").SourceKind,
                Is.EqualTo("ConstructorDefault"));
            Assert.That(explanation.Values.Single(value => value.MemberName == "Retries").SourceKind,
                Is.EqualTo("ConstructorDefault"));
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Build_ShouldGenerateOnlyForParametersWithoutADefault()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("constructor no default", TestMethods.Placeholder);

        var command = Proto.Context.Data().For<CreateInvoice>().With(x => x.Total, 1m).Build();

        Assert.That(command.Reference, Does.StartWith("CreateInvoice.Reference-"),
            "a parameter without a default is still generated");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task Explain_ShouldExposeProvenanceAndReuseResolvedValues()
    {
        await using var host = CreateHost();
        await host.StartAsync();
        await host.StartTestAsync("explain data", TestMethods.Placeholder);

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
            .AddData(data => data.Values.Use<InvoiceId>(
                context => new InvoiceId(context.NextGuid())))
            .AddData(data => data.For<DomainInvoice>()
                .Default(x => x.Total, 25m)
                .ConstructUsing(context => DomainInvoice.Create(
                    context.Value<InvoiceId>(nameof(DomainInvoice.Id)),
                    context.Value<decimal>(nameof(DomainInvoice.Total)))));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("domain factory", TestMethods.Placeholder);

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

}
