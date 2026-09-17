namespace ProtoTest.Demo;

using System.Net;
using ProtoTest.Core;
using ProtoTest.GraphQL;
using ProtoTest.Json;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;
using ProtoTest.SampleApp.Testing;

[Application(SampleAppTargets.Api)]
[SampleEnvironment]
[RestAuth<SampleUserAuthenticator>]
[GraphQLAuth<SampleUserAuthenticator>]
public sealed class GraphQLControlPlaneTests
{
    [ProtoTest]
    [SampleUser]
    public async Task SubscriptionStreamsShapeMatchedEvents()
    {
        var expected = new
        {
            id = JsonValue.GreaterThan(0),
            product = "live-notebook",
            total = 15m,
            status = "pending"
        };
        await using var subscription = await Proto.Context.GraphQL()
            .Subscription("orderCreated")
            .Select(expected)
            .SubscribeAsync();

        // graphql-transport-ws acknowledges the connection, not each subscribe message.
        await Task.Delay(100);

        using var created = await Proto.Context.GraphQL()
            .Mutation("createOrder", new
            {
                input = Gql.Variable(
                    "CreateOrderInput!",
                    new CreateOrderRequest("live-notebook", 1, 15m))
            })
            .Select(new { id = Gql.Field })
            .ExecuteAsync();
        created.ShouldHaveNoErrors();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var notification = await subscription.ExpectNextAsync(expected, timeout.Token);
        notification.ShouldHaveNoErrors();
    }

    [ProtoTest]
    [SampleUser]
    public async Task MutationVariablesAndUploadsStaySimple()
    {
        var command = new CreateOrderRequest("notebook", 2, 12.50m);
        using var created = await Proto.Context.GraphQL()
            .Mutation("createOrder", new
            {
                input = Gql.Variable("CreateOrderInput!", command)
            })
            .ExpectAsync(new
            {
                id = JsonValue.GreaterThan(0),
                product = "notebook",
                total = 25m,
                status = "pending"
            });
        created.ShouldHaveNoErrors();

        var expectedUpload = new
        {
            fileName = "example.txt",
            contentType = "text/plain",
            length = JsonValue.GreaterThan(0)
        };
        using var uploaded = await Proto.Context.GraphQL()
            .Mutation("uploadDocument", new
            {
                file = Gql.Upload("ProtoTest GraphQL"u8.ToArray(), "example.txt", "text/plain")
            })
            .Select(expectedUpload)
            .ExecuteAsync();
        uploaded.ShouldHaveNoErrors().ShouldMatchData(expectedUpload);
    }

    [ProtoTest]
    [SampleUser]
    public async Task FilteringSortingAndPagingReadLikeTheGraphQLContract()
    {
        await CreateOrderAsync("notebook-basic", 1, 10m);
        await CreateOrderAsync("notebook-pro", 2, 20m);
        await CreateOrderAsync("monitor", 1, 100m);

        using var response = await Proto.Context.GraphQL()
            .Query("FindNotebooks", query => query
                .Connection("orders", orders => orders
                    .Where(filter => filter.Contains("product", "notebook"))
                    .OrderBy(order => order.Descending("total"))
                    .First(1)
                    .Nodes("product", "total", "status")
                    .PageInfo("hasNextPage", "hasPreviousPage")
                    .TotalCount()))
            .ExecuteAsync();

        response.ShouldHaveNoErrors().ShouldMatchData(new
        {
            orders = new
            {
                nodes = new[] { new { product = "notebook-pro", total = 40m, status = "pending" } },
                pageInfo = new { hasNextPage = true, hasPreviousPage = false },
                totalCount = 2
            }
        });
    }

    [ProtoTest]
    [SampleUser]
    public async Task TypedSelectionCanStayOwnedByTheTest()
    {
        var user = Proto.Context.Resolve<SampleUserContext>();
        using var response = await Proto.Context.GraphQL()
            .Query("me")
            .Select<ViewerSelection>()
            .ExecuteAsync();

        var viewer = response.ReadDataAs<ViewerSelection>();
        response.ShouldHaveNoErrors().ShouldMatchData(new ViewerSelection(user.Id, user.Email, user.Role));
        Assert.That(viewer, Is.EqualTo(new ViewerSelection(user.Id, user.Email, user.Role)));
    }

    [ProtoTest]
    [SampleUser]
    public async Task RawDocumentsRemainAvailableForAliasesAndFragments()
    {
        using var response = await Proto.Context.GraphQL()
            .Request(
                """
                query ViewerCard {
                  viewer: me {
                    ...ViewerFields
                  }
                }

                fragment ViewerFields on UserResponse {
                  id
                  email
                  role
                }
                """,
                "ViewerCard")
            .ExecuteAsync();

        response.ShouldHaveNoErrors().ShouldMatchData(new
        {
            viewer = new
            {
                id = JsonValue.NotNull(),
                email = JsonValue.StringContaining("@example.test"),
                role = SampleRoles.Member
            }
        });
    }

    [ProtoTest]
    [SampleUser(SampleRoles.TenantAdministrator)]
    public async Task RestProvisioningIsImmediatelyVisibleThroughGraphQLControlPlane()
    {
        using var provisioned = await Proto.Context.Rest()
            .Body(new CreateWorkspaceRequest("analytics", "eu-central", "growth"))
            .PostAsync("/api/workspaces");
        provisioned.ShouldHaveHttpStatus(HttpStatusCode.Created);

        using var workspaces = await Proto.Context.GraphQL()
            .Query("workspaces")
            .ExpectAsync(new[]
            {
                new { name = "analytics", region = "eu-central", plan = "growth" }
            });
        workspaces.ShouldHaveNoErrors();

        using var controlPlane = await Proto.Context.GraphQL()
            .Query("controlPlane")
            .ExpectAsync(new
            {
                userCount = 1,
                workspaceCount = 1,
                releaseCount = 0,
                monthlyRecurringRevenue = 199m
            });
        controlPlane.ShouldHaveNoErrors();
    }

    [ProtoTest]
    [SampleUser]
    public async Task AnonymousGraphQLRequestExplainsAuthenticationFailure()
    {
        using var response = await Proto.Context.GraphQL()
            .WithoutAuth()
            .Query("me")
            .Select(new { id = Gql.Field })
            .ExecuteAsync();
        response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
    }

    private static async Task CreateOrderAsync(string product, int quantity, decimal unitPrice)
    {
        using var response = await Proto.Context.GraphQL()
            .Mutation("createOrder", new
            {
                input = Gql.Variable(
                    "CreateOrderInput!",
                    new CreateOrderRequest(product, quantity, unitPrice))
            })
            .Select(new { id = Gql.Field })
            .ExecuteAsync();
        response.ShouldHaveNoErrors();
    }

    private sealed record ViewerSelection(string Id, string Email, string Role);
}
