namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class TargetedGraphQLProbeTests
{
    [Test]
    public async Task ShouldMatchShape_WithNullData_ShouldThrowTheGraphQlAssertion()
    {
        await using var host = CreateHost("""{"data":null,"errors":[{"message":"boom"}]}""");
        await host.StartTestAsync("null data shape", "01", Method());
        try
        {
            using var response = await Proto.Context.GraphQL()
                .Query(null, query => query.Field("value"))
                .ExecuteAsync();

            var exception = Assert.Throws<GraphQLAssertionException>(() =>
                response.ShouldMatchShape(new { value = 1 }));
            Assert.That(exception!.Message, Does.Contain("Expected GraphQL data"));
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHost CreateHost(string payload)
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphQL => graphQL.AddClient("Default", "https://example.test/graphql", http =>
            http.ConfigurePrimaryHttpMessageHandler(() => new StubHandler(payload))));
        return builder.Build();
    }

    private static MethodInfo Method() => typeof(TargetedGraphQLProbeTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }

    private sealed class StubHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(payload) });
    }
}
