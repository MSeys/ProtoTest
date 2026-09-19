namespace ProtoTest.GraphQL.Tests;

using System.Net;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Http;
using NUnit.Framework;

[TestFixture]
public sealed class ApplicationGraphQLTests
{
    [Test]
    public async Task Application_ShouldResolveTheDefaultGraphQlClient()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:ControlPlane:BaseUrl"] = "https://app.test",
                ["ProtoTest:Applications:ControlPlane:Endpoints:GraphQL"] = "/graphql"
            }));
        builder.AddApplication("ControlPlane", app => app.AddGraphQL(graphql => graphql.AddClient("ControlPlane")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("graphql application", TestMethod(), [new ApplicationAttribute("ControlPlane")]);

        Assert.That(context.Client<HttpClient>("ControlPlane:ControlPlane").BaseAddress,
            Is.EqualTo(new Uri("https://app.test/graphql")));

        context.GraphQL();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var entry = host.Trace.Snapshot().Tests.Single().Entries
            .Single(item => item.Kind == "graphql.builder.create");
        Assert.Multiple(() =>
        {
            Assert.That(entry.Attributes["client.name"], Is.EqualTo("ControlPlane"));
            Assert.That(entry.Attributes["application.name"], Is.EqualTo("ControlPlane"));
        });
    }

    [Test]
    public async Task Application_ShouldFallBackToTheTransportRootedAtTheGraphQlEndpoint()
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
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // The client is named "Api", so only GraphQL's own default endpoint yields "/graphql".
                ["ProtoTest:Applications:ControlPlane:Endpoints:GraphQL"] = "/graphql"
            }));
        builder.AddApplication("ControlPlane", app =>
        {
            app.AddGraphQL(graphql => graphql.AddClient("Api"));
            // Stands in for AddAspNetCoreServer: the transport the client has no URL to bypass.
            app.Services.AddSingleton(new ProtoApplicationTransport("ControlPlane", "ControlPlane"));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("ControlPlane", "http://transport.test/", handler));
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "graphql transport fallback", TestMethod(), [new ApplicationAttribute("ControlPlane")]);

        using var response = await context.GraphQL()
            .Query(null, query => query.Field("ping"))
            .ExecuteAsync();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.That(requestedUri, Is.EqualTo(new Uri("http://transport.test/graphql")));
        Assert.That(context.Client<HttpClient>("ControlPlane:Api").BaseAddress, Is.Null);
    }

    private static MethodInfo TestMethod()
        => typeof(ApplicationGraphQLTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }

    private sealed class StubTransportInitializer(string name, string baseAddress, HttpMessageHandler handler)
        : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.RegisterClient(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) }, Name);
            return Task.FromResult(true);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }
}
