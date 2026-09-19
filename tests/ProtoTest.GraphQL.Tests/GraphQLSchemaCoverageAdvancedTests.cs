namespace ProtoTest.GraphQL.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class GraphQLSchemaCoverageAdvancedTests
{
    private const string Schema = """
        schema { query: RootQuery }
        interface Node { id: ID! }
        type RootQuery { node(id: ID!): Node search(filter: SearchInput): [Node!]! }
        type Product implements Node { id: ID! name: String! }
        extend type Product { price: Float @deprecated(reason: "Use priceInfo") }
        input SearchInput { term: String nested: NestedInput }
        input NestedInput { enabled: Boolean tags: [String!] }
        """;

    [Test]
    public void Collector_ShouldFollowAliasesInlineFragmentsAndNamedFragments()
    {
        var collector = new GraphQLSchemaCoverageCollector("Catalog", Schema);
        collector.Collect(Observation("""
            query Find($id: ID!) {
              result: node(id: $id) { id ... on Product { name ...Price } }
            }
            fragment Price on Product { price }
            """, "Find", """{"id":"42"}"""));

        var items = Flatten(collector.GetReportItems()).ToDictionary(item => item.Identifier);
        Assert.Multiple(() =>
        {
            Assert.That(items["RootQuery.node"].IsCovered, Is.True);
            Assert.That(items["RootQuery.node(id)"].IsCovered, Is.True);
            Assert.That(items["Node.id"].IsCovered, Is.True);
            Assert.That(items["Product.name"].IsCovered, Is.True);
            Assert.That(items["Product.price"].IsCovered, Is.True);
            Assert.That(items["Product.price"].Metadata!["deprecated"], Is.True);
        });
    }

    [Test]
    public void Collector_ShouldFollowNestedVariableInputPropertiesAndCountRepeatedHits()
    {
        var collector = new GraphQLSchemaCoverageCollector("Catalog", Schema);
        var observation = Observation(
            "query Search($filter: SearchInput) { search(filter: $filter) { id } }",
            "Search", """{"filter":{"term":"book","nested":{"enabled":true,"tags":["a"]}}}""");
        collector.Collect(observation);
        collector.Collect(observation);

        var items = Flatten(collector.GetReportItems()).ToDictionary(item => item.Identifier);
        Assert.Multiple(() =>
        {
            Assert.That(items["SearchInput.term"].Count, Is.EqualTo(2));
            Assert.That(items["SearchInput.nested"].Count, Is.EqualTo(2));
            Assert.That(items["NestedInput.enabled"].Count, Is.EqualTo(2));
            Assert.That(items["NestedInput.tags"].Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void Collector_ShouldCountAFragmentSpreadReachedFromTwoSiblingsOnce()
    {
        var collector = new GraphQLSchemaCoverageCollector("Catalog", Schema);
        collector.Collect(Observation("""
            query Search {
              first: node(id: "1") { ...Details }
              second: node(id: "2") { ...Details }
            }
            fragment Details on Node { id }
            """, "Search", "{}"));

        var items = Flatten(collector.GetReportItems()).ToDictionary(item => item.Identifier);
        Assert.Multiple(() =>
        {
            Assert.That(items["RootQuery.node"].Count, Is.EqualTo(2));
            Assert.That(items["Node.id"].Count, Is.EqualTo(1));
        });
    }

    private static ProtoObservation Observation(string document, string operation, string variables)
        => new("Catalog", "graphql.response", operation,
            new GraphQLResponseData("query", operation, document, 200, 0, [], TimeSpan.Zero, variables));

    private static IEnumerable<ProtoReportItem> Flatten(IEnumerable<ProtoReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is not null)
                foreach (var child in Flatten(item.Children)) yield return child;
        }
    }
}
