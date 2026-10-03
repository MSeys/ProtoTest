namespace ProtoTest.GraphQL.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// Schema coverage from a path the in-process application serves: the schema loads once the run's
/// infrastructure started, before the first test, without an address or a committed copy.
/// </summary>
[TestFixture]
public sealed class SchemaFromApplicationTests
{
    [TestCase(AspNetCoreServerLifetime.PerRun)]
    [TestCase(AspNetCoreServerLifetime.PerTest)]
    public async Task SchemaCoverage_ShouldLoadTheSchemaTheInProcessApplicationServes(AspNetCoreServerLifetime lifetime)
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<ProtoTest.AspNetCore.SampleApi.Program>(lifetime: lifetime)
            .AddGraphQL(graphql => graphql
                .AddClient("Api")
                .WithSchemaCoverage("/schema.graphql")));
        await using var host = builder.Build();

        await host.StartAsync();

        var context = await host.StartTestAsync("schema", TestMethods.Placeholder, [new ApplicationAttribute("Api")]);
        var collector = context.Services.GetServices<IProtoCollector>().OfType<GraphQLSchemaCoverageCollector>().Single();
        var items = collector.GetReportItems().Flatten().ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(items.Select(item => item.Identifier), Does.Contain("Query.orders(first)"));
            Assert.That(
                items.Single(item => item.Identifier == ProtoSpecIdentity.ReportIdentifier).Metadata![ProtoSpecIdentity.SourceMetadataKey],
                Is.EqualTo("/schema.graphql"),
                "the identity names the path the application serves");
        }
    }

    [Test]
    public async Task SchemaCoverage_ShouldFailTheStartNamingAPathTheApplicationDoesNotServe()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<ProtoTest.AspNetCore.SampleApi.Program>()
            .AddGraphQL(graphql => graphql
                .AddClient("Api")
                .WithSchemaCoverage("/missing.graphql")));
        await using var host = builder.Build();

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(failure!.Message, Does.Contain("'/missing.graphql'"));
    }
}
