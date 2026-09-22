namespace ProtoTest.Http.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoHttpClientResolverTests
{
    [Test]
    public async Task Resolve_ShouldRootTheRestTransportFallbackAtTheRegisteredEndpoint()
    {
        await using var host = BuildHost("Orders", "Rest", "Orders", "Orders", "/api/orders").Build();
        await host.StartTestAsync("rest fallback", "01", TestMethod(), [new ApplicationAttribute("Orders")]);

        try
        {
            var resolution = ProtoHttpClientResolver.Resolve(Proto.Context, "Rest");

            Assert.Multiple(() =>
            {
                Assert.That(resolution.RequestedName, Is.EqualTo("Orders"));
                Assert.That(resolution.ResolvedName, Is.EqualTo("Orders:Orders"));
                Assert.That(resolution.ApplicationName, Is.EqualTo("Orders"));
                // The in-process transport resolver roots this client, so it is the one named.
                Assert.That(resolution.EndpointResolver, Is.EqualTo("transport"));
                Assert.That(resolution.Client.BaseAddress, Is.EqualTo(new Uri("http://transport.test/")));
            });
            Assert.That(resolution.BaseAddressResolver, Is.Not.Null);
            Assert.That(
                await resolution.BaseAddressResolver!(Proto.Context, CancellationToken.None),
                Is.EqualTo(new Uri("http://transport.test/api/orders")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Resolve_ShouldRootTheTransportFallbackAtTheEndpointTheClientRegistered()
    {
        await using var host = BuildHost("Headless", "GraphQL", "Api", "Api", "/graphql").Build();
        await host.StartTestAsync("graphql fallback", "02", TestMethod(), [new ApplicationAttribute("Headless")]);

        try
        {
            var resolution = ProtoHttpClientResolver.Resolve(Proto.Context, "GraphQL");

            Assert.Multiple(() =>
            {
                Assert.That(resolution.RequestedName, Is.EqualTo("Api"));
                Assert.That(resolution.ResolvedName, Is.EqualTo("Headless:Api"));
            });
            Assert.That(resolution.BaseAddressResolver, Is.Not.Null);
            Assert.That(
                await resolution.BaseAddressResolver!(Proto.Context, CancellationToken.None),
                Is.EqualTo(new Uri("http://transport.test/graphql")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Resolve_ShouldKeepProtocolClientsApartWhenTheyShareAName()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("Rest", "Api", "http://rest.test/"));
            services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer("GraphQL", "Api", "http://graphql.test/"));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("scoped clients", "03", TestMethod());

        try
        {
            var rest = ProtoHttpClientResolver.Resolve(Proto.Context, "Rest", "Api");
            var graphql = ProtoHttpClientResolver.Resolve(Proto.Context, "GraphQL", "Api");

            Assert.Multiple(() =>
            {
                Assert.That(rest.Client.BaseAddress, Is.EqualTo(new Uri("http://rest.test/")));
                Assert.That(graphql.Client.BaseAddress, Is.EqualTo(new Uri("http://graphql.test/")));
            });
        }
        finally { await host.CompleteTestAsync(); }
    }

    private static ProtoHostBuilder BuildHost(
        string applicationName,
        string protocolName,
        string clientName,
        string endpointName,
        string endpointPath)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [$"ProtoTest:Applications:{applicationName}:Endpoints:{endpointName}"] = endpointPath
            }));
        builder.AddApplication(applicationName, app =>
        {
            app.RegisterClient(protocolName, clientName);
            app.Services.AddSingleton(new ProtoHttpClientEndpointRegistration(
                ProtoHttpClientRegistration.Qualify(clientName, applicationName), endpointName));
            // Stands in for AddAspNetCoreServer: the transport the accessor falls back to without a base URL.
            app.Services.AddSingleton(new ProtoApplicationTransport(applicationName, applicationName));
            app.Services.AddSingleton<IProtoClientInitializer>(
                new StubTransportInitializer(null, applicationName, "http://transport.test/"));
        });
        return builder;
    }

    private static MethodInfo TestMethod()
        => typeof(ProtoHttpClientResolverTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }

    private sealed class StubTransportInitializer(string? protocol, string name, string baseAddress) : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public string? Protocol { get; } = protocol;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.RegisterClient(
                new HttpClient { BaseAddress = new Uri(baseAddress) },
                ProtoClientResolution.ScopedName(Protocol, Name));
            return Task.FromResult(true);
        }
    }
}
