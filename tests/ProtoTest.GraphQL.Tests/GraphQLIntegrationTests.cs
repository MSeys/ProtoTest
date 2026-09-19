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
using ProtoTest.Rest;

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

            response.ShouldHaveNoErrors().ShouldMatchShape(new
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
                var entries = host.Trace.Snapshot().Tests.Single().Entries;
                var request = entries.Single(entry => entry.Kind == "graphql.operation");
                Assert.That(entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "graphql.builder.create"));
                Assert.That(request.Attributes["auth.outcome"], Is.EqualTo("skipped"));
                Assert.That(entries, Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "assert.graphql.no_errors" && entry.ParentId == request.Id));
                var shape = entries.Single(entry => entry.Kind == "assert.json.shape");
                Assert.That(shape.ParentId, Is.EqualTo(request.Id));
                Assert.That(shape.Attributes["shape.result"], Is.EqualTo("matched"));
                Assert.That(shape.Attributes["shape.matches"], Does.Contain("$.products"));
                Assert.That(shape.Attributes["shape.expected"], Is.Not.Empty);
                Assert.That(shape.Attributes["shape.actual"], Is.Not.Empty);
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
                ["ProtoTest:Applications:Catalog:BaseUrl"] = "https://example.test/graphql",
                ["ProtoTest:Applications:Catalog:GraphQL:Schema"] =
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
    public async Task SchemaCollector_ShouldSeeObservationsUnderTheApplicationsQualifiedTargetName()
    {
        // Arrange: under an application the client is registered as "Catalog:Api", so observations must
        // carry that target name or the schema collector keyed on it goes blind.
        const string schema = """
            type Query { product: Product }
            type Product { id: ID! name: String! }
            """;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"product":{"id":"42","name":"Notebook"}}}""")
        });
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Catalog:BaseUrl"] = "https://app.test",
                ["ProtoTest:Applications:Catalog:Endpoints:GraphQL"] = "/graphql"
            }));
        builder.AddApplication("Catalog", app => app.AddGraphQL(graphQL => graphQL
            .AddClient("Api", configure: http => http.ConfigurePrimaryHttpMessageHandler(() => handler))
            .WithSchemaCoverage(schema)));
        await using var host = builder.Build();
        await host.StartTestAsync("application coverage", "11", TestMethod(), [new ApplicationAttribute("Catalog")]);
        try
        {
            // Act
            using var response = await Proto.Context.GraphQL()
                .Query("Product", query => query.Field("product", field => field.Fields("id", "name")))
                .ExecuteAsync();
            response.ShouldHaveNoErrors();

            // Assert
            var collector = Proto.Context.Services.GetServices<IProtoCollector>()
                .OfType<GraphQLSchemaCoverageCollector>()
                .Single();
            var fields = Flatten(collector.GetReportItems())
                .Where(item => item.Category == "GraphQL field")
                .ToDictionary(item => item.Identifier);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(fields["Query.product"].IsCovered, Is.True);
                Assert.That(fields["Product.name"].IsCovered, Is.True);
                Assert.That(fields["Product.id"].IsCovered, Is.True);
            }
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
                ["ProtoTest:Applications:Catalog:BaseUrl"] = "https://configured.example/graphql"
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
    public async Task ConfigureResponses_ShouldLetKnownConfigurationSectionOverrideCodeDefaults()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:GraphQL:Responses:MaxResponseBodyBytes"] = "2048"
            }));
        builder.AddGraphQL(graphQL => graphQL.ConfigureResponses(options => options.MaxResponseBodyBytes = 4096));
        await using var host = builder.Build();
        await host.StartTestAsync(
            "graphql response configuration",
            "13",
            (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            var options = Proto.Context.ResolveResponseOptions(ProtoGraphQLBuilder.ProtocolName);

            Assert.Multiple(() =>
            {
                Assert.That(options.MaxResponseBodyBytes, Is.EqualTo(2048));
                Assert.That(options.ConfigurationSectionName, Is.EqualTo("ProtoTest:GraphQL:Responses"));
            });
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
            response.ShouldMatchShape(new { login = new { token = "server-secret" } });

            Assert.That(Proto.Context.Attachments.Select(item => item.Name.Split('-', 2)[1]), Is.EquivalentTo(new[]
            {
                "graphql-01-request", "graphql-01-response", "graphql-01-expected-shape"
            }));
            var request = Proto.Context.Attachments.Single(item => item.Name.EndsWith("graphql-01-request", StringComparison.Ordinal));
            var text = Encoding.UTF8.GetString(await request.ReadAllBytesAsync());
            Assert.That(text, Does.Not.Contain("client-secret"));
            Assert.That(text, Does.Contain("[REDACTED]"));
            var expectedShape = Proto.Context.Attachments.Single(item =>
                item.Name.EndsWith("graphql-01-expected-shape", StringComparison.Ordinal));
            var expectedShapeText = Encoding.UTF8.GetString(await expectedShape.ReadAllBytesAsync());
            Assert.That(expectedShapeText, Does.Not.Contain("server-secret"));
            Assert.That(expectedShapeText, Does.Contain("[REDACTED]"));
            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            Assert.That(observation.VariablesJson, Does.Not.Contain("client-secret"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task CaptureAttachments_ShouldLetKnownConfigurationSectionOverrideCodeDefaults()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:GraphQL:Attachments:CaptureRequestBodies"] = "false",
                ["ProtoTest:GraphQL:Attachments:CaptureResponses"] = "true",
                ["ProtoTest:GraphQL:Attachments:CaptureExpectedShapes"] = "false"
            }));
        builder.AddGraphQL(graphQL => graphQL.CaptureAttachments(options =>
        {
            options.CaptureResponses = false;
            options.CaptureExpectedShapes = true;
        }));
        await using var host = builder.Build();
        await host.StartTestAsync(
            "graphql attachment configuration",
            "12",
            (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            var options = Proto.Context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);

            Assert.That(options, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(options!.CaptureRequestBodies, Is.False);
                Assert.That(options.CaptureResponses, Is.True);
                Assert.That(options.CaptureExpectedShapes, Is.False);
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ResponseAndAttachmentConfiguration_ShouldBeIsolatedFromRest()
    {
        // GraphQL registers first this time: the response options must still be GraphQL's, and the
        // attachments REST registers last must still be REST's own.
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Rest:Responses:MaxResponseBodyBytes"] = "2048",
                ["ProtoTest:GraphQL:Responses:MaxResponseBodyBytes"] = "4096",
                ["ProtoTest:Rest:Attachments:CaptureResponses"] = "false",
                ["ProtoTest:GraphQL:Attachments:CaptureResponses"] = "true"
            }));
        builder.AddGraphQL(graphQL => graphQL.CaptureAttachments());
        builder.AddRest(rest => rest.CaptureAttachments());
        await using var host = builder.Build();
        await host.StartTestAsync(
            "graphql protocol options isolation",
            "16",
            (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            // Each protocol resolves its options under its own key and never another protocol's.
            var graphQLResponses = Proto.Context.Services.GetKeyedService<ProtoHttpResponseOptions>(ProtoGraphQLBuilder.ProtocolName)!;
            var restResponses = Proto.Context.Services.GetKeyedService<ProtoHttpResponseOptions>(ProtoRestBuilder.ProtocolName)!;
            var graphQLAttachments = Proto.Context.Services.GetKeyedService<ProtoHttpAttachmentOptions>(ProtoGraphQLBuilder.ProtocolName)!;
            var restAttachments = Proto.Context.Services.GetKeyedService<ProtoHttpAttachmentOptions>(ProtoRestBuilder.ProtocolName)!;

            Assert.Multiple(() =>
            {
                Assert.That(graphQLResponses.ConfigurationSectionName, Is.EqualTo("ProtoTest:GraphQL:Responses"));
                Assert.That(graphQLResponses.MaxResponseBodyBytes, Is.EqualTo(4096));
                Assert.That(restResponses.ConfigurationSectionName, Is.EqualTo("ProtoTest:Rest:Responses"));
                Assert.That(restResponses.MaxResponseBodyBytes, Is.EqualTo(2048));
                Assert.That(graphQLAttachments.CaptureResponses, Is.True);
                Assert.That(restAttachments.CaptureResponses, Is.False);
                Assert.That(graphQLAttachments, Is.Not.SameAs(restAttachments));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task HeaderTrace_ShouldRecordCountAndNamesWithoutValues()
    {
        const string secret = "super-secret-token";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"ping":"pong"}}""")
        });
        await using var host = CreateHost(handler);
        await host.StartTestAsync("header trace", "14", TestMethod());

        try
        {
            using var response = await Proto.Context.GraphQL()
                .Header("Authorization", $"Bearer {secret}")
                .Header("X-Trace", "first")
                .Header("x-trace", "second")
                .Query(null, query => query.Field("ping"))
                .ExecuteAsync();
            response.ShouldHaveNoErrors();

            var snapshot = host.Trace.Snapshot();
            var entries = snapshot.Tests.Single().Entries;
            var operation = entries.Single(entry => entry.Kind == "graphql.operation");
            var headerEvents = entries.Where(entry => entry.Kind == "http.header.configure").ToArray();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(operation.Attributes["http.request.header_count"], Is.EqualTo("2"));
                Assert.That(headerEvents, Has.Length.EqualTo(2));
                Assert.That(headerEvents.Select(entry => entry.Attributes["http.header.name"]),
                    Is.EquivalentTo(new[] { "Authorization", "X-Trace" }));
                Assert.That(headerEvents.Select(entry => entry.Attributes["http.header.value_recorded"]),
                    Is.All.EqualTo("false"));
                Assert.That(JsonSerializer.Serialize(snapshot), Does.Not.Contain(secret));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Variables_ShouldBeTruncatedToTheConfiguredDiagnosticLength()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"search":[]}}""")
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.CaptureAttachments(options => options.MaxDiagnosticBodyLength = 64);
            graphQL.AddClient("Default", "https://example.test/graphql", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("variables truncation", "17", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("Search", query => query.Field("search"))
                .Variables(new { term = new string('x', 512) })
                .ExecuteAsync();

            var observation = (GraphQLResponseData)Proto.Context.RecordedObservations
                .Single(item => item.Kind == "graphql.response").Data!;
            Assert.Multiple(() =>
            {
                Assert.That(observation.VariablesJson, Does.Contain("truncated"));
                Assert.That(observation.VariablesJson!.Length, Is.LessThan(512));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task AddClientFrom_ShouldReuseTheSourceClientThroughTheAlias()
    {
        Uri? requestedUri = null;
        var handler = new StubHandler(request =>
        {
            requestedUri = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"ping":"pong"}}""")
            };
        });
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL =>
        {
            graphQL.AddClient("Catalog", "https://source.example/api/", http =>
                http.ConfigurePrimaryHttpMessageHandler(() => handler));
            graphQL.AddClientFrom("CatalogV2", "Catalog", "v2/");
            graphQL.AddClientFrom("CatalogAlias", "Catalog");
        });
        await using var host = builder.Build();
        await host.StartTestAsync("alias client", "13", TestMethod());
        try
        {
            // A path-rooted alias keeps its prefix; without one the default GraphQL endpoint is used.
            using var versioned = await Proto.Context.GraphQL("CatalogV2")
                .Query(null, query => query.Field("ping"))
                .ExecuteAsync();
            Assert.That(requestedUri, Is.EqualTo(new Uri("https://source.example/api/v2/")));

            using var aliased = await Proto.Context.GraphQL("CatalogAlias")
                .Query(null, query => query.Field("ping"))
                .ExecuteAsync();
            Assert.That(requestedUri, Is.EqualTo(new Uri("https://source.example/graphql")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Errors_ShouldTolerateMalformedMessagesAndPaths()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"errors":[{"message":42,"path":["a",{"bad":true},null,1.5,2]},{"path":"not-an-array"},7]}""")
        });
        await using var host = CreateHost(handler);
        await host.StartTestAsync("malformed errors", "18", TestMethod());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            response.ShouldHaveErrors();
            Assert.Multiple(() =>
            {
                Assert.That(response.Errors, Has.Count.EqualTo(3));
                Assert.That(response.Errors[0].Message, Is.Empty);
                Assert.That(response.Errors[0].Path[0], Is.EqualTo("a"));
                Assert.That(response.Errors[0].Path[1], Is.EqualTo("""{"bad":true}"""));
                Assert.That(response.Errors[0].Path[2], Is.EqualTo("null"));
                Assert.That(response.Errors[0].Path[3], Is.EqualTo("1.5"));
                Assert.That(response.Errors[0].Path[4], Is.EqualTo(2));
                Assert.That(response.Errors[1].Message, Is.Empty);
                Assert.That(response.Errors[1].Path, Is.Empty);
                Assert.That(response.Errors[2].Message, Is.Empty);
            });
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
