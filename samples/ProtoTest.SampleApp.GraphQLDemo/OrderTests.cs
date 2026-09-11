namespace ProtoTest.SampleApp.GraphQLDemo;

using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[GraphQLClient(SampleAppTargets.GraphQL)]
[SampleEnvironment]
[GraphQLAuth<SampleGraphQLAuthenticator>]
public sealed class OrderTests
{
    [ProtoTest]
    [SampleUser]
    public async Task MemberCanCreateAndPageThroughOrders()
    {
        using var created = await Proto.Context.GraphQL()
            .Mutation("CreateOrder", mutation => mutation
                .Variable("input", GqlType.Named("CreateOrderInput").NonNull())
                .Field("createOrder", field => field
                    .Argument("input", Gql.Var("input"))
                    .Fields("id", "product", "quantity", "total", "status")))
            .Variables(new { input = new CreateOrderRequest("notebook", 2, 12.50m) })
            .ExecuteAsync();

        created.ShouldHaveNoErrors().ShouldMatchData(new
        {
            createOrder = new { id = JsonValue.GreaterThan(0), product = "notebook", quantity = 2, total = 25m, status = "pending" }
        });

        using var listed = await Proto.Context.GraphQL()
            .Query("Orders", query => query.Connection("orders", orders => orders
                .Where(filter => filter.Contains("product", "note").GreaterThan("total", 20m))
                .OrderBy(order => order.Descending("total"))
                .First(10)
                .Nodes("id", "product", "total")
                .PageInfo()
                .TotalCount()))
            .ExecuteAsync();

        listed.ShouldHaveNoErrors().ShouldMatchData(new
        {
            orders = new
            {
                nodes = new[] { new { id = JsonValue.GreaterThan(0), product = "notebook", total = 25m } },
                pageInfo = new { hasNextPage = false, hasPreviousPage = false, startCursor = JsonValue.NotNull(), endCursor = JsonValue.NotNull() },
                totalCount = 1
            }
        });
    }

    [ProtoTest]
    [SampleUser]
    public async Task RequestCanExplicitlyOptOutOfAuthentication()
    {
        using var response = await Proto.Context.GraphQL()
            .WithoutAuth()
            .Query("Me", query => query.Field("me", me => me.Fields("id")))
            .ExecuteAsync();

        response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
    }
}
