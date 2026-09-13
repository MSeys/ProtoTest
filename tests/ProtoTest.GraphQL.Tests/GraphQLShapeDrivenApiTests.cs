namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Json;

[TestFixture]
public sealed class GraphQLShapeDrivenApiTests
{
    [Test]
    public async Task ExpectAsync_ShouldDeriveQuerySelectionAndMatchRootField()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = Document(request);
            return Json("""{"data":{"orders":{"nodes":[{"id":42,"product":"notebook","total":25}],"pageInfo":{"hasNextPage":false},"totalCount":1}}}""");
        });
        await host.StartTestAsync("shape-query", "1", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("orders", new
                {
                    first = 10,
                    where = new { product = new { contains = "note" } },
                    order = new[] { new { total = Gql.Enum("DESC") } }
                })
                .ExpectAsync(new
                {
                    nodes = new[] { new { id = JsonValue.GreaterThan(0), product = "notebook", total = 25m } },
                    pageInfo = new { hasNextPage = false },
                    totalCount = 1
                });

            Assert.Multiple(() =>
            {
                Assert.That(document, Does.Contain("orders(first: 10"));
                Assert.That(document, Does.Contain("query Orders"));
                Assert.That(document, Does.Contain("where: {product: {contains: \"note\"}}"));
                Assert.That(document, Does.Contain("order: [{total: DESC}]") );
                Assert.That(document, Does.Contain("nodes {"));
                Assert.That(document, Does.Contain("pageInfo {"));
                Assert.That(document, Does.Not.Contain("contains\n"));
                Assert.That(Proto.Context.RecordedObservations.Select(item => item.Kind),
                    Does.Contain("graphql.contract.shape"));
                Assert.That(Proto.Context.RecordedObservations
                    .Where(item => item.Kind == "graphql.response")
                    .Select(item => ((GraphQLResponseData)item.Data!).OperationName),
                    Is.EquivalentTo(new[] { "Orders" }));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ExpectAsync_ShouldSupportScalarRootFields()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"ping":"pong"}}"""));
        await host.StartTestAsync("scalar", "2", Method());
        try
        {
            using var response = await Proto.Context.GraphQL().Query("ping").ExpectAsync("pong");
            response.ShouldHaveNoErrors();
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Mutation_ShouldAcceptAnonymousInputAndInferExpectedSelection()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = Document(request);
            return Json("""{"data":{"createOrder":{"id":7,"product":"notebook"}}}""");
        });
        await host.StartTestAsync("shape-mutation", "3", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Mutation("createOrder", new { input = new RenamedInput("notebook", 2) })
                .ExpectAsync(new { id = JsonValue.GreaterThan(0), product = "notebook" });

            Assert.That(document, Does.Contain("mutation CreateOrder {"));
            Assert.That(document, Does.Contain("createOrder(input: {product: \"notebook\", quantity: 2})"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task SelectOfType_ShouldDeriveSelectionsFromTestOwnedContracts()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = Document(request);
            return Json("""{"data":{"orders":{"nodes":[],"pageInfo":{"hasNextPage":false}}}}""");
        });
        await host.StartTestAsync("typed", "4", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("orders")
                .Select<OrdersSelection>()
                .ExecuteAsync();

            var data = response.ReadDataAs<OrdersSelection>();
            response.ShouldMatchData(new OrdersSelection([], new PageInfoSelection(false)));

            Assert.Multiple(() =>
            {
                Assert.That(document, Does.Contain("nodes {"));
                Assert.That(document, Does.Contain("id"));
                Assert.That(document, Does.Contain("product"));
                Assert.That(document, Does.Contain("pageInfo {"));
                Assert.That(document, Does.Contain("hasNextPage"));
                Assert.That(data, Is.Not.Null);
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Select_ShouldSupportFieldMarkersAndJsonPropertyNames()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = Document(request);
            return Json("""{"data":{"viewer":{"displayName":"Ada"}}}""");
        });
        await host.StartTestAsync("markers", "5", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("viewer")
                .Select(new { displayName = Gql.Field })
                .ExecuteAsync();
            response.ShouldHaveNoErrors();
            Assert.That(document, Does.Contain("displayName"));

            using var typed = await Proto.Context.GraphQL()
                .Query("viewer")
                .Select<RenamedSelection>()
                .ExecuteAsync();
            Assert.That(document, Does.Contain("displayName"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Select_ShouldExplainWhenNoShapeDrivenOperationWasStarted()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"id":1}}"""));
        await host.StartTestAsync("invalid-shape", "6", Method());
        try
        {
            Assert.That(() => Proto.Context.GraphQL().Select(new { id = Gql.Field }),
                Throws.InvalidOperationException.With.Message.Contains("shape-driven Query or Mutation"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(response))));
        return builder.Build();
    }

    private static string Document(HttpRequestMessage request)
    {
        using var envelope = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return envelope.RootElement.GetProperty("query").GetString()!;
    }

    private static HttpResponseMessage Json(string content)
        => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private static MethodInfo Method() => typeof(GraphQLShapeDrivenApiTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }

    private sealed record OrdersSelection(IReadOnlyList<OrderSelection> Nodes, PageInfoSelection PageInfo);
    private sealed record OrderSelection(int Id, string Product);
    private sealed record PageInfoSelection(bool HasNextPage);
    private sealed record RenamedSelection([property: JsonPropertyName("displayName")] string Name);
    private sealed record RenamedInput(
        [property: JsonPropertyName("product")] string Name,
        int Quantity);
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
