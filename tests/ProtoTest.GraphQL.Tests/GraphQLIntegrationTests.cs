namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Json;

[TestFixture]
public sealed class GraphQLIntegrationTests
{
    [Test]
    public async Task FluentQuery_ShouldRenderExecuteAssertAndObserve()
    {
        string? requestBody = null;
        var handler = new StubHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"products":{"nodes":[{"id":"42","name":"Notebook","price":12.5}],"pageInfo":{"hasNextPage":false,"endCursor":"end"}}}}""")
            };
        });
        await using var host = CreateHost(handler);
        await host.StartTestAsync("query", "1", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("FindProducts", query => query
                    .Connection("products", products => products
                        .Where(filter => filter.Contains("name", "note").LessThan("price", 100m))
                        .OrderBy(order => order.Ascending("name"))
                        .First(10)
                        .Nodes("id", "name", "price")
                        .PageInfo("hasNextPage", "endCursor")))
                .ExecuteAsync();

            response.ShouldHaveNoErrors().ShouldMatchData(new
            {
                products = new
                {
                    nodes = new[] { new { id = "42", name = JsonValue.StringContaining("book"), price = JsonValue.LessThan(20m) } },
                    pageInfo = new { hasNextPage = false, endCursor = JsonValue.NotNull() }
                }
            });

            using var envelope = JsonDocument.Parse(requestBody!);
            var queryText = envelope.RootElement.GetProperty("query").GetString()!;
            Assert.Multiple(() =>
            {
                Assert.That(queryText, Does.Contain("query FindProducts"));
                Assert.That(queryText, Does.Contain("where: {name: {contains: \"note\"}, price: {lt: 100}}"));
                Assert.That(queryText, Does.Contain("order: [{name: ASC}]"));
                Assert.That(queryText, Does.Contain("first: 10"));
                Assert.That(Proto.Context.RecordedObservations.Select(o => o.Kind),
                    Is.EquivalentTo(new[] { "graphql.response", "graphql.contract.shape" }));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public void SchemaCollector_ShouldReportCoveredAndUncoveredFields()
    {
        const string schema = """
            type Query { product: Product products(where: ProductFilterInput): [Product!]! }
            type Product { id: ID! name: String! price: Float! }
            input ProductFilterInput { name: StringFilterInput price: FloatFilterInput }
            input StringFilterInput { contains: String eq: String }
            input FloatFilterInput { lt: Float gt: Float }
            """;
        var collector = new GraphQLSchemaCoverageCollector("Catalog", schema);
        collector.Collect(new ProtoObservation(
            "Catalog", "graphql.response", "query GetProduct",
            new GraphQLResponseData("query", "GetProduct",
                "query GetProduct($where: ProductFilterInput) { product { id name } products(where: $where) { id } }",
                200, 0, [], TimeSpan.Zero, """{"where":{"price":{"lt":100}}}""")));

        var fields = Flatten(collector.GetReportItems()).Where(item => item.Category == "GraphQL field").ToDictionary(item => item.Identifier);
        var allItems = Flatten(collector.GetReportItems()).ToDictionary(item => item.Identifier);
        Assert.Multiple(() =>
        {
            Assert.That(fields["Query.product"].IsCovered, Is.True);
            Assert.That(fields["Query.products"].IsCovered, Is.True);
            Assert.That(fields["Product.id"].IsCovered, Is.True);
            Assert.That(fields["Product.name"].IsCovered, Is.True);
            Assert.That(fields["Product.price"].IsCovered, Is.False);
            Assert.That(allItems["Query.products(where)"].IsCovered, Is.True);
            Assert.That(allItems["ProductFilterInput.price"].IsCovered, Is.True);
            Assert.That(allItems["ProductFilterInput.name"].IsCovered, Is.False);
            Assert.That(allItems["FloatFilterInput.lt"].IsCovered, Is.True);
            Assert.That(allItems["FloatFilterInput.gt"].IsCovered, Is.False);
        });
    }

    [Test]
    public async Task SchemaCoverage_ShouldResolveSingleConfiguredSchemaSource()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Clients:Catalog:BaseUrl"] = "https://example.test/graphql",
                ["ProtoTest:Clients:Catalog:GraphQL:Schema"] =
                    "type Query { product: Product } type Product { id: ID! name: String! }"
            }));
        builder.AddGraphQL(graphQL => graphQL
            .AddClient("Catalog")
            .WithSchemaCoverage());
        await using var host = builder.Build();
        await host.StartTestAsync("schema-config", "7", TestMethod());
        try
        {
            var collector = Proto.Context.Services.GetServices<IProtoCollector>()
                .OfType<GraphQLSchemaCoverageCollector>()
                .Single();
            Assert.That(Flatten(collector.GetReportItems()).Select(item => item.Identifier),
                Does.Contain("Product.name"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task GraphQLErrors_ShouldRemainInspectable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"product":null},"errors":[{"message":"Missing","path":["product"],"extensions":{"code":"NOT_FOUND"}}]}""")
        });
        await using var host = CreateHost(handler);
        await host.StartTestAsync("errors", "2", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("Missing", query => query
                    .Variable("id", GqlType.Id.NonNull())
                    .Field("product", field => field.Argument("id", Gql.Var("id"))))
                .Variables(new { id = "missing" })
                .ExecuteAsync();
            response.ShouldHaveErrors().ShouldHaveError("NOT_FOUND");
            Assert.That(response.Errors.Single().Path, Is.EqualTo(new object[] { "product" }));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task AddClient_ShouldResolveBaseUrlFromSharedClientConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Clients:Catalog:BaseUrl"] = "https://configured.example/graphql"
            }));
        builder.AddGraphQL(graphQL => graphQL.AddClient("Catalog"));
        await using var host = builder.Build();
        await host.StartTestAsync("configured", "3", TestMethod());
        try
        {
            Assert.That(Proto.Context.Client<HttpClient>("Catalog").BaseAddress,
                Is.EqualTo(new Uri("https://configured.example/graphql")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task AddClient_ShouldResolvePerTestBaseAddress()
    {
        Uri? requestedUri = null;
        var handler = new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"ping":"pong"}}""") };
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient(
            "Catalog",
            _ => new Uri("https://dynamic.example/graphql"),
            http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        await using var host = builder.Build();
        await host.StartTestAsync("dynamic", "4", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL("Catalog")
                .Query(null, query => query.Field("ping"))
                .ExecuteAsync();
            Assert.That(requestedUri, Is.EqualTo(new Uri("https://dynamic.example/graphql")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ResponseLimit_ShouldRejectOversizedPayloadAndRecordFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"value":"too large"}}""")
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.ConfigureResponses(options => options.MaxResponseBodyBytes = 4);
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("limited", "5", TestMethod());
        try
        {
            Assert.ThrowsAsync<ProtoResponseTooLargeException>(async () => await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync());
            Assert.That(Proto.Context.RecordedObservations.Single().Kind, Is.EqualTo("graphql.failure"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Attachments_ShouldRedactSensitiveRequestAndExpectedShapeValues()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"login":{"token":"server-secret"}}}""")
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.CaptureAttachments();
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("attachments", "6", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Mutation("Login", mutation => mutation.Field("login", field => field
                    .Argument("password", Gql.Var("password"))
                    .Fields("token")))
                .Variables(new { password = "client-secret" })
                .ExecuteAsync();
            response.ShouldMatchData(new { login = new { token = "server-secret" } });

            Assert.That(Proto.Context.Attachments.Select(item => item.Name.Split('-', 2)[1]), Is.EquivalentTo(new[]
            {
                "graphql-01-request", "graphql-01-response", "graphql-01-expected-shape"
            }));
            var request = Proto.Context.Attachments.Single(item => item.Name.EndsWith("graphql-01-request", StringComparison.Ordinal));
            var text = Encoding.UTF8.GetString(await request.ReadAllBytesAsync());
            Assert.That(text, Does.Not.Contain("client-secret"));
            Assert.That(text, Does.Contain("[REDACTED]"));
            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            Assert.That(observation.VariablesJson, Does.Not.Contain("client-secret"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(HttpMessageHandler handler)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => handler)));
        return builder.Build();
    }

    private static MethodInfo TestMethod() => typeof(GraphQLIntegrationTests).GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }

    private static IEnumerable<ProtoReportItem> Flatten(IEnumerable<ProtoReportItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.Children is not null)
                foreach (var child in Flatten(item.Children)) yield return child;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
