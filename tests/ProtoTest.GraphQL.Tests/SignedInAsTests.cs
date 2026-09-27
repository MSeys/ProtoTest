namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Http;

/// <summary>
/// GraphQL rides the same authentication hook and HTTP applier as REST, so the built-in test user
/// reaches a GraphQL request the same way.
/// </summary>
[TestFixture]
public sealed class SignedInAsTests
{
    [Test]
    public async Task SignedInAs_ShouldCarryTheTestUserOnGraphQlRequests()
    {
        HttpRequestMessage? captured = null;
        var handler = new StubHttpHandler(request =>
        {
            captured = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":{"ping":"pong"}}""")
            };
        });
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<ProtoTest.AspNetCore.SampleApi.Program>()
            .AddGraphQL(graphql => graphql.AddClient(
                "Api",
                "https://app.test/graphql",
                configure: http => http.ConfigurePrimaryHttpMessageHandler(() => handler))));
        await using var host = builder.Build();
        var method = SignedInTestMethod();
        var context = await host.StartTestAsync("graphql test user", method, ProtoAttributeResolver.Resolve(method));

        using var response = await context.GraphQL()
            .Query(null, query => query.Field("ping"))
            .ExecuteAsync();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.Headers.TryGetValues(ProtoTestUserHeader.HeaderName, out var values), Is.True,
                "the GraphQL request carries the test user");
            Assert.That(ProtoTestUserHeader.TryDecode(values!.Single(), out var user), Is.True);
            Assert.That(user!.Name, Is.EqualTo("gina"));
            Assert.That(user.Roles, Is.EqualTo(new[] { "auditor" }));
        }
    }

    private static MethodInfo SignedInTestMethod()
        => typeof(SignedInCases).GetMethod(nameof(SignedInCases.SignedIn), BindingFlags.Instance | BindingFlags.Public)!;

    [Application("Api")]
    public sealed class SignedInCases
    {
        [SignedInAs("gina", "auditor")]
        public void SignedIn()
        {
        }
    }
}
