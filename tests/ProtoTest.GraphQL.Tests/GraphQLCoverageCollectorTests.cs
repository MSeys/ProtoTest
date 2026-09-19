namespace ProtoTest.GraphQL.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class GraphQLCoverageCollectorTests
{
    [Test]
    public void OperationCollector_ShouldAggregateGraphQLResponsesAndIgnoreOtherKinds()
    {
        var collector = new GraphQLCoverageCollector("Catalog");
        var response = new ProtoObservation("Catalog", "graphql.response", "query FindProducts");
        var shape = new ProtoObservation("Catalog", "graphql.contract.shape", "query FindProducts");
        var otherTarget = new ProtoObservation("Other", "graphql.response", "query FindProducts");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collector.CanCollect(response), Is.True);
            Assert.That(collector.CanCollect(shape), Is.False);
            Assert.That(collector.CanCollect(otherTarget), Is.False);
        }

        collector.Collect(response);
        collector.Collect(response);

        var item = collector.GetReportItems().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(collector.Category, Is.EqualTo("GraphQL operation"));
            Assert.That(item.Category, Is.EqualTo("GraphQL operation"));
            Assert.That(item.Kind, Is.EqualTo(ProtoReportItemKinds.Coverage));
            Assert.That(item.Identifier, Is.EqualTo("query FindProducts"));
            Assert.That(item.IsCovered, Is.True);
            Assert.That(item.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void OperationCollector_ShouldKeepCaseDistinctOperationNamesApart()
    {
        var collector = new GraphQLCoverageCollector("Catalog");
        collector.Collect(new ProtoObservation("Catalog", "graphql.response", "query FindProducts"));
        collector.Collect(new ProtoObservation("Catalog", "graphql.response", "query findproducts"));

        var items = collector.GetReportItems().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Length.EqualTo(2));
            Assert.That(items.Select(item => item.Identifier),
                Is.EquivalentTo(new[] { "query FindProducts", "query findproducts" }));
        });
    }
}
