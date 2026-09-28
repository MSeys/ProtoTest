namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http.Authenticators;
using ProtoTest.Json;

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
        await host.StartTestAsync("fluent", "1", TestMethods.Placeholder);
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

            response.Should.HaveNoErrors();
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
        await host.StartTestAsync("literals", "2", TestMethods.Placeholder);
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
        await host.StartTestAsync("paging", "3", TestMethods.Placeholder);
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
        await host.StartTestAsync("protocol", "4", TestMethods.Placeholder);
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
    public async Task ShouldMatchShape_WithoutData_ShouldThrowTheGraphQlAssertion()
    {
        await using var host = CreateHost(_ => Json("""{"errors":[{"message":"boom"}]}"""));
        await host.StartTestAsync("shape-no-data", "10", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() =>
                response.Should.MatchShape(new { value = 1 }));
            Assert.That(exception!.Message, Does.Contain("Expected GraphQL data"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task MatchShape_ShouldNameTheOperationOnMismatch()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1}}"""));
        await host.StartTestAsync("shape-subject", "14", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() =>
                response.Should.MatchShape(new { value = 2 }));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception!.Message, Does.StartWith(
                    "query <anonymous> - Shape mismatch failed with 1 error(s):"));
                Assert.That(exception.Message, Does.Contain("$.value"));
                Assert.That(exception.InnerException, Is.TypeOf<ProtoTest.Json.JsonShapeMismatchException>(),
                    "the shared mismatch data stays reachable");
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task MatchShape_Exact_ShouldRejectFieldsTheShapeDoesNotMention()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1,"extra":true}}"""));
        await host.StartTestAsync("shape exact", "24", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            response.Should.MatchShape(new { value = 1 });

            var exception = Assert.Throws<GraphQLAssertionException>(() =>
                response.Should.MatchShape(new { value = 1 }, exact: true));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception!.Message, Does.StartWith(
                    "query <anonymous> - Shape mismatch failed with 1 error(s):"));
                Assert.That(exception.Message, Does.Contain("$.extra"));
                Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
                Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                    "the shared mismatch data stays reachable");
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task MatchShape_Exact_ShouldPassWhenEveryFieldIsMentioned()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1,"customer":{"email":"ada@example.test"}}}"""));
        await host.StartTestAsync("shape exact success", "25", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var returned = response.Should.MatchShape(
                new { value = 1, customer = new { email = "ada@example.test" } },
                exact: true);

            Assert.That(returned, Is.SameAs(response));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task MatchShape_Exact_ShouldTreatAValueConstraintAsMentioningItsSubtree()
    {
        await using var host = CreateHost(_ => Json(
            """{"data":{"value":1,"customer":{"email":"ada@example.test","phone":"555"}}}"""));
        await host.StartTestAsync("shape exact constraint", "26", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            response.Should.MatchShape(new { value = 1, customer = JsonValue.NotNull() }, exact: true);
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task MatchShape_Exact_ShouldKeepIgnoringExtraFieldsWithoutExact()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1,"extra":true}}"""));
        await host.StartTestAsync("shape partial", "27", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            Assert.DoesNotThrow(() => response.Should.MatchShape(new { value = 1 }));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ShouldMatchShape_WithoutData_ShouldRecordTheFailedAssertionInTheTrace()
    {
        await using var host = CreateHost(_ => Json("""{"errors":[{"message":"boom"}]}"""));
        await host.StartTestAsync("shape-no-data trace", "13", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("value"))
            .ExecuteAsync();

        var exception = Assert.Throws<GraphQLAssertionException>(() =>
            response.Should.MatchShape(new { value = 1 }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.json.shape");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["graphql.operation"], Is.EqualTo("query <anonymous>"));
            Assert.That(operation.Attributes["shape.result"], Is.EqualTo("mismatched"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
        });
    }

    [Test]
    public async Task Response_ShouldExposeHttpStatusDataAndExtensions()
    {
        await using var host = CreateHost(_ => new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("""{"data":{"value":42},"extensions":{"traceId":"abc"}}""")
        });
        await host.StartTestAsync("response", "5", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();
            response.Should.HaveHttpStatus(HttpStatusCode.Accepted).Should.HaveNoErrors();
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
    public async Task ShouldNot_HaveHttpStatus_Should_Pass_When_Status_Differs()
    {
        await using var host = CreateHost(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"value":1}}""")
        });
        await host.StartTestAsync("negated status pass", "11", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            response.ShouldNot.HaveHttpStatus(HttpStatusCode.Accepted);
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ShouldNot_HaveHttpStatus_Should_Fail_With_Negated_Message_And_Record_The_Check()
    {
        await using var host = CreateHost(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"data":{"value":1}}""")
        });
        await host.StartTestAsync("negated status fail", "12", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("value"))
            .ExecuteAsync();

        var exception = Assert.Throws<GraphQLAssertionException>(
            () => response.ShouldNot.HaveHttpStatus(HttpStatusCode.OK));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("Expected HTTP status not 200 (OK), but received 200 (OK)"));
            Assert.That(exception!.Message, Does.Contain("Response body:"),
                "a failed status reports the sanitized body the same way REST does");
        });
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.http.status");
        Assert.Multiple(() =>
        {
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["expected.status_code"], Is.EqualTo("200"));
            Assert.That(operation.Attributes["actual.status_code"], Is.EqualTo("200"));
            Assert.That(operation.Attributes["assertion.negated"], Is.EqualTo("true"));
            Assert.That(operation.Sections![0].Kind, Is.EqualTo(ProtoTraceSectionKind.Checks));
            Assert.That(operation.Sections![0].Items![0].Tone, Is.EqualTo(ProtoTraceSectionTone.Error));
            Assert.That(operation.Sections![0].Items![0].Detail, Does.Contain("not"));
        });
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
        await host.StartTestAsync("auth", "6", TestMethods.Placeholder);
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
        await host.StartTestAsync("subscription", "7", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("orderCreated")
                .Select(new { id = Gql.Field, status = Gql.Field })
                .SubscribeAsync();

            using var message = await subscription.NextAsync();
            message!.Should.HaveNoErrors().Should.MatchShape(new { id = 42, status = "pending" });
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
        await host.StartTestAsync("subscription-api", "8", TestMethods.Placeholder);
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
    public async Task Header_WithAnInvalidName_ShouldThrowInsteadOfBeingSilentlyDropped()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1}}"""));
        await host.StartTestAsync("invalid header", "10", TestMethods.Placeholder);
        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(() => Proto.Context.GraphQL()
                .Header("Bad Header", "value")
                .Query(null, query => query.Field("value"))
                .ExecuteAsync());

            Assert.That(exception!.Message, Does.Contain("Bad Header"));
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
        await host.StartTestAsync("subscription-error", "9", TestMethods.Placeholder);
        try
        {
            await using var subscription = await Proto.Context.GraphQL()
                .Subscription("restricted")
                .Select(new { id = Gql.Field })
                .SubscribeAsync();
            using var message = await subscription.NextAsync();

            message!.Should.HaveErrors().Should.HaveError("FORBIDDEN");
            Assert.That(subscription.IsCompleted, Is.True);
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Should_ErrorAssertions_ShouldChainWithStatusAndShape()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1}}"""));
        await host.StartTestAsync("error chain", "20", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var returned = response.Should.HaveHttpStatus(HttpStatusCode.OK)
                .Should.HaveNoErrors()
                .Should.MatchShape(new { value = 1 });

            Assert.That(returned, Is.SameAs(response));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Should_ErrorAssertions_ShouldKeepTheExistingMessagesAndEvidence()
    {
        await using var host = CreateHost(_ => Json("""{"errors":[{"message":"boom","extensions":{"code":"FORBIDDEN"}}]}"""));
        await host.StartTestAsync("error facade", "21", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("value"))
            .ExecuteAsync();

        var noErrors = Assert.Throws<GraphQLAssertionException>(() => response.Should.HaveNoErrors());
        Assert.That(response.Should.HaveErrors(), Is.SameAs(response));
        Assert.That(response.Should.HaveError("forbidden"), Is.SameAs(response));
        var wrongCode = Assert.Throws<GraphQLAssertionException>(() => response.Should.HaveError("OTHER"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(noErrors!));
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        var operation = entries.Single(entry => entry.Kind == "assert.graphql.no_errors");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(noErrors!.Message, Is.EqualTo("Expected no GraphQL errors, but received 1: boom"));
            Assert.That(wrongCode!.Message, Is.EqualTo("Expected a GraphQL error with code 'OTHER', but found: FORBIDDEN."));
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["actual.error_count"], Is.EqualTo("1"));
            Assert.That(operation.Attributes.ContainsKey("assertion.negated"), Is.False,
                "the positive path keeps the existing evidence");
            Assert.That(operation.Name, Is.EqualTo("Assert no GraphQL errors"),
                "the positive operation names are unchanged");
            Assert.That(entries.Single(entry => entry.Kind == "assert.graphql.has_errors").Name,
                Is.EqualTo("Assert GraphQL has errors"));
            Assert.That(
                entries.Single(entry => entry.Kind == "assert.graphql.error_code"
                    && entry.Outcome == ProtoTraceOutcome.Succeeded).Name,
                Is.EqualTo("Assert GraphQL error · forbidden"));
        }
    }

    [Test]
    public async Task ShouldNot_ErrorAssertions_ShouldAskForTheOpposite()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":1}}"""));
        await host.StartTestAsync("negated errors", "22", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("value"))
            .ExecuteAsync();

        response.ShouldNot.HaveErrors();
        response.ShouldNot.HaveError("FORBIDDEN");

        var thrown = Assert.Throws<GraphQLAssertionException>(() => response.ShouldNot.HaveNoErrors());

        await host.CompleteTestAsync(ProtoTestResult.Failed(thrown!));
        var entries = host.Trace.Snapshot().Tests.Single().Entries;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(thrown!.Message, Is.EqualTo("Expected GraphQL errors, but the response contained none."));
            Assert.That(entries.Single(entry => entry.Kind == "assert.graphql.has_errors").Name,
                Is.EqualTo("Assert GraphQL has no errors"));
            Assert.That(entries.Single(entry => entry.Kind == "assert.graphql.error_code").Name,
                Is.EqualTo("Assert GraphQL does not have an error with code 'FORBIDDEN'"));
            Assert.That(entries.Single(entry => entry.Kind == "assert.graphql.no_errors").Name,
                Is.EqualTo("Assert GraphQL has errors"));
        }
    }

    [Test]
    public async Task Obsolete_ErrorAssertions_ShouldStillDelegateToTheFacade()
    {
        await using var host = CreateHost(_ => Json("""{"errors":[{"message":"boom","extensions":{"code":"FORBIDDEN"}}]}"""));
        await host.StartTestAsync("obsolete errors", "23", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("value"))
            .ExecuteAsync();

        // Intentional: pins the obsolete shims while they delegate to the facade; CS0618 is expected.
#pragma warning disable CS0618
        Assert.That(response.ShouldHaveErrors().ShouldHaveError("FORBIDDEN"), Is.SameAs(response));
        var thrown = Assert.Throws<GraphQLAssertionException>(() => response.ShouldHaveNoErrors());
#pragma warning restore CS0618

        Assert.That(thrown!.Message, Is.EqualTo("Expected no GraphQL errors, but received 1: boom"));
        await host.CompleteTestAsync();
    }

    [Test]
    public async Task ReadDataAs_WithPath_ShouldReadSingleValuesAndIndices()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":42,"order":{"total":19.95},"items":[1,2,3]}}"""));
        await host.StartTestAsync("path read", "30", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(response.ReadDataAs<int>("$.value"), Is.EqualTo(42));
                Assert.That(response.ReadDataAs<decimal>("order.total"), Is.EqualTo(19.95m));
                Assert.That(response.ReadDataAs<int>("$.items[2]"), Is.EqualTo(3));
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadDataAs_WithPath_ShouldNameTheOperationAndPathWhenMissing()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":42}}"""));
        await host.StartTestAsync("path missing", "31", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadDataAs<int>("$.missing"));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exception!.Message, Does.StartWith(
                    "query <anonymous> - The JSON path '$.missing' did not match: the member 'missing' was not found."));
                Assert.That(exception.InnerException, Is.TypeOf<ProtoTest.Json.JsonPathException>());
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadDataAs_WithPath_ShouldThrowOnWrongTypeAndReturnNullForJsonNull()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":42,"text":"hello","note":null}}"""));
        await host.StartTestAsync("path types", "32", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.Throws<JsonException>(() => response.ReadDataAs<int>("$.text"));
                Assert.That(
                    Assert.Throws<GraphQLAssertionException>(() => response.ReadDataAs<int>("$.value.x"))!.Message,
                    Does.Contain("cannot be read from a JSON number"));
                Assert.That(response.ReadDataAs<string?>("$.note"), Is.Null);
            }
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadRequired_ShouldReturnTheValueAndThrowWhenNothingIsThere()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":42}}"""));
        await host.StartTestAsync("required read", "33", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            Assert.That(response.ReadRequired<ValueData>(), Is.Not.Null);
            Assert.That(response.ReadRequired<int>("$.value"), Is.EqualTo(42));
            Assert.That(
                Assert.Throws<GraphQLAssertionException>(() => response.ReadRequired<int>("$.missing"))!.Message,
                Does.Contain("$.missing"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadRequired_ShouldThrowNamingTheOperationWhenThereIsNoData()
    {
        await using var host = CreateHost(_ => Json("""{"errors":[{"message":"boom"}]}"""));
        await host.StartTestAsync("required no data", "36", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadRequired<int>());

            Assert.That(exception!.Message, Is.EqualTo(
                "query <anonymous> - ReadRequired<Int32> failed: the response did not contain data."));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadRequired_WithPath_ShouldThrowWhenThePathIsJsonNull()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"note":null}}"""));
        await host.StartTestAsync("required null", "34", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("note"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadRequired<string>("$.note"));

            Assert.That(exception!.Message, Is.EqualTo(
                "query <anonymous> - ReadRequired<String>('$.note') failed: the value at '$.note' was JSON null."));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadRequired_WithPath_ShouldFailTheDeserializeOperationOnAJsonNullValueType()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"note":null}}"""));
        await host.StartTestAsync("required null value type", "37", TestMethods.Placeholder);
        using var response = await Proto.Context.GraphQL()
            .Query(null, query => query.Field("note"))
            .ExecuteAsync();

        var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadRequired<int>("$.note"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "graphql.response.deserialize");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Is.EqualTo(
                "query <anonymous> - ReadRequired<Int32>('$.note') failed: the value at '$.note' was JSON null."));
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Attributes["target.type"], Is.EqualTo(typeof(int).FullName));
            Assert.That(operation.Attributes["graphql.path"], Is.EqualTo("$.note"));
            Assert.That(operation.Error!.Type, Is.EqualTo(typeof(GraphQLAssertionException).FullName));
        }
    }

    [Test]
    public async Task ReadRequired_ShouldThrowOnJsonNullDataForAValueType()
    {
        await using var host = CreateHost(_ => Json("""{"data":null}"""));
        await host.StartTestAsync("required null data value type", "38", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() => response.ReadRequired<int>());

            Assert.That(exception!.Message, Is.EqualTo(
                "query <anonymous> - ReadRequired<Int32> failed: the response did not contain data."));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task ReadRequired_ShouldChainWithShapeAssertions()
    {
        await using var host = CreateHost(_ => Json("""{"data":{"value":42}}"""));
        await host.StartTestAsync("required chain", "35", TestMethods.Placeholder);
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var value = response.Should.HaveNoErrors()
                .Should.MatchShape(new { value = 42 })
                .ReadRequired<int>("$.value");

            Assert.That(value, Is.EqualTo(42));
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new StubHttpHandler(response)))
            .WithSubscriptionTransport(GraphQLSubscriptionTransport.Sse));
        return builder.Build();
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    private static string ReadDocument(HttpRequestMessage request)
    {
        using var envelope = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
        return envelope.RootElement.GetProperty("query").GetString()!;
    }

    private sealed record ValueData(int Value);
}
