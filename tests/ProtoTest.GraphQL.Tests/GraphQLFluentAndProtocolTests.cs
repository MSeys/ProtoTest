namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class GraphQLFluentAndProtocolTests
{
    [Test]
    public async Task Mutation_ShouldRenderVariablesAliasesAndNestedSelections()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = ReadDocument(request);
            return Json("""{"data":{"placed":{"id":"1","customer":{"name":"Ada"}}}}""");
        });
        await host.StartTestAsync("fluent", "1", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Mutation("PlaceOrder", mutation => mutation
                    .Variable("input", GqlType.Named("OrderInput").NonNull())
                    .Field("createOrder", order => order
                        .Alias("placed")
                        .Argument("input", Gql.Var("input"))
                        .Select(selection => selection
                            .Field("id")
                            .Field("customer", customer => customer.Fields("name")))))
                .Variables(new { input = new { product = "notebook", quantity = 2 } })
                .ExecuteAsync();

            response.ShouldHaveNoErrors();
            Assert.That(document, Does.Contain("mutation PlaceOrder($input: OrderInput!)"));
            Assert.That(document, Does.Contain("placed: createOrder(input: $input)"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Arguments_ShouldRenderEnumsListsNullAndEscapedStrings()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = ReadDocument(request);
            return Json("""{"data":{"search":[]}}""");
        });
        await host.StartTestAsync("literals", "2", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("search", field => field
                    .Argument("states", new[] { Gql.Enum("OPEN"), Gql.Enum("CLOSED") })
                    .Argument("term", "a\"b")
                    .Argument("optional", null)))
                .ExecuteAsync();
            Assert.That(document, Does.Contain("states: [OPEN, CLOSED], term: \"a\\\"b\", optional: null"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Connection_ShouldRenderBackwardPagingAndDefaultPageInfo()
    {
        string? document = null;
        await using var host = CreateHost(request =>
        {
            document = ReadDocument(request);
            return Json("""{"data":{"orders":{"nodes":[],"pageInfo":{"hasNextPage":false,"hasPreviousPage":true,"startCursor":null,"endCursor":null}}}}""");
        });
        await host.StartTestAsync("paging", "3", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query("Previous", query => query.Connection("orders", orders => orders
                    .Last(5).Before("cursor-10").Nodes("id").PageInfo()))
                .ExecuteAsync();
            Assert.Multiple(() =>
            {
                Assert.That(document, Does.Contain("last: 5"));
                Assert.That(document, Does.Contain("before: \"cursor-10\""));
                Assert.That(document, Does.Contain("hasPreviousPage"));
                Assert.That(document, Does.Contain("startCursor"));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [TestCase("not json", "valid JSON")]
    [TestCase("{}", "'data' or 'errors'")]
    public async Task InvalidGraphQLResponse_ShouldProduceProtocolDiagnostic(string payload, string expectedMessage)
    {
        await using var host = CreateHost(_ => Json(payload));
        await host.StartTestAsync("protocol", "4", Method());
        try
        {
            var exception = Assert.ThrowsAsync<GraphQLProtocolException>(() => Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync());
            Assert.That(exception!.Message, Does.Contain(expectedMessage));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Response_ShouldExposeHttpStatusDataAndExtensions()
    {
        await using var host = CreateHost(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("""{"data":{"value":42},"extensions":{"traceId":"abc"}}""")
        });
        await host.StartTestAsync("response", "5", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();
            response.ShouldHaveHttpStatus(HttpStatusCode.Accepted).ShouldHaveNoErrors();
            Assert.Multiple(() =>
            {
                Assert.That(response.HasData, Is.True);
                Assert.That(response.Extensions!.Value.GetProperty("traceId").GetString(), Is.EqualTo("abc"));
                Assert.That(response.ReadDataAs<ValueData>()!.Value, Is.EqualTo(42));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task RequestAuthentication_ShouldApplyAndCanBeDisabled()
    {
        var authorizations = new List<AuthenticationHeaderValue?>();
        await using var host = CreateHost(request =>
        {
            authorizations.Add(request.Headers.Authorization);
            return Json("""{"data":{"value":1}}""");
        });
        await host.StartTestAsync("auth", "6", Method());
        try
        {
            using var authenticated = await Proto.Context.GraphQL()
                .Auth(new BearerTokenAuthenticator("secret"))
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();
            using var anonymous = await Proto.Context.GraphQL()
                .Auth(new BearerTokenAuthenticator("secret"))
                .WithoutAuth()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            Assert.Multiple(() =>
            {
                Assert.That(authorizations[0]?.Scheme, Is.EqualTo("Bearer"));
                Assert.That(authorizations[0]?.Parameter, Is.EqualTo("secret"));
                Assert.That(authorizations[1], Is.Null);
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Subscription_ShouldReadSseEventsAndReuseShapeAssertionsAndCoverageObservations()
    {
        string? accept = null;
        string? document = null;
        await using var host = CreateHost(request =>
        {
            accept = request.Headers.Accept.Single().MediaType;
            document = ReadDocument(request);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    event: next
                    data: {"data":{"orderCreated":{"id":42,"status":"pending"}}}

                    event: complete
                    data:

                    """,
                    System.Text.Encoding.UTF8,
                    "text/event-stream")
            };
        });
        await host.StartTestAsync("subscription", "7", Method());
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field, status = Gql.Field })
                .SubscribeAsync();

            using var message = await subscription.NextAsync();
            message!.ShouldHaveNoErrors().ShouldMatchData(new { id = 42, status = "pending" });
            Assert.That(await subscription.NextAsync(), Is.Null);

            Assert.Multiple(() =>
            {
                Assert.That(accept, Is.EqualTo("text/event-stream"));
                Assert.That(document, Does.Contain("subscription OrderCreated"));
                Assert.That(subscription.IsCompleted, Is.True);
                Assert.That(Proto.Context.RecordedObservations
                    .Where(item => item.Kind == "graphql.response")
                    .Select(item => ((GraphQLResponseData)item.Data!).OperationType),
                    Does.Contain("subscription"));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ExecuteAsync_ShouldDirectSubscriptionsToStreamingApi()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1}}"""));
        await host.StartTestAsync("subscription-api", "8", Method());
        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Proto.Context.GraphQL()
                .Subscription("value")
                .Select(new { id = Gql.Field })
                .ExecuteAsync());
            Assert.That(exception!.Message, Does.Contain("SubscribeAsync"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Subscription_ShouldExposeProtocolErrorEventsAsGraphQLResponses()
    {
        await using var host = CreateHost(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                event: error
                data: [{"message":"Subscription denied","extensions":{"code":"FORBIDDEN"}}]

                """,
                System.Text.Encoding.UTF8,
                "text/event-stream")
        });
        await host.StartTestAsync("subscription-error", "9", Method());
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("restricted")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();
            using var message = await subscription.NextAsync();

            message!.ShouldHaveErrors().ShouldHaveError("FORBIDDEN");
            Assert.That(subscription.IsCompleted, Is.True);
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(response)))
            .WithSubscriptionTransport(GraphQLSubscriptionTransport.Sse));
        return builder.Build();
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private static string ReadDocument(HttpRequestMessage request)
    {
        using var envelope = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return envelope.RootElement.GetProperty("query").GetString()!;
    }

    private static MethodInfo Method() => typeof(GraphQLFluentAndProtocolTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }
    private sealed record ValueData(int Value);
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

}
