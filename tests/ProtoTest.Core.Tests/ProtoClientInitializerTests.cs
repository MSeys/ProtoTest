namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public class ProtoClientInitializerTests
{
    [Test]
    public async Task Initializers_Should_Fall_Through_In_Registration_Order()
    {
        var calls = new List<string>();
        var builder = new ProtoHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("Api", context =>
                    {
                        calls.Add("First");
                        return false;
                    }));
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("api", context =>
                    {
                        calls.Add("Second");
                        context.RegisterClient(new TestClient(), "api");
                        return true;
                    }));
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("Api", context =>
                    {
                        calls.Add("Third");
                        context.RegisterClient(new TestClient(), "Api");
                        return true;
                    }));
            });

        await using var host = builder.Build();
        await host.StartTestAsync("InitializerOrder", "00001", TestMethods.Placeholder);

        try
        {
            Assert.That(calls, Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(Proto.Context.Client<TestClient>("API"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task Initializers_With_The_Same_Name_And_Different_ClientTypes_Should_Both_Run()
    {
        var builder = new ProtoHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("Api", context =>
                    {
                        context.RegisterClient(new TestClient(), "Api");
                        return true;
                    }));
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<OtherTestClient>("Api", context =>
                    {
                        context.RegisterClient(new OtherTestClient(), "Api");
                        return true;
                    }));
            });

        await using var host = builder.Build();
        await host.StartTestAsync("InitializerTypes", "00002", TestMethods.Placeholder);

        try
        {
            Assert.That(Proto.Context.Client<TestClient>("Api"), Is.Not.Null);
            Assert.That(Proto.Context.Client<OtherTestClient>("Api"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task UnscopedInitializer_ShouldServeEveryProtocolChainOnce()
    {
        var fallbackCalls = 0;
        var fallback = new TestInitializer<TestClient>("Default", context =>
        {
            fallbackCalls++;
            context.RegisterClient(new TestClient(), "Default");
            return true;
        });
        var builder = new ProtoHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("Default", _ => false, protocol: "Rest"));
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("Default", _ => false, protocol: "GraphQL"));
                services.AddSingleton<IProtoClientInitializer>(fallback);
            });

        await using var host = builder.Build();
        await host.StartTestAsync("SharedFallback", "00003", TestMethods.Placeholder);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(fallbackCalls, Is.EqualTo(1), "an unscoped provider serves every chain with one invocation");
                Assert.That(Proto.Context.Client<TestClient>("Default"), Is.Not.Null);
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task UnscopedInitializer_ShouldReuseOnlyTheRegistrationMadeForTheChainName()
    {
        // Audit 5 A5-24: the fallback memo is keyed by (provider, chain name). Two protocol chains
        // whose names differ only in case share the provider's one invocation, and each chain's lookup
        // resolves the client registered under that name.
        var fallbackCalls = 0;
        var fallback = new TestInitializer<TestClient>("Api", context =>
        {
            fallbackCalls++;
            context.RegisterClient(new TestClient(), "Api");
            return true;
        });
        var builder = new ProtoHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("api", _ => false, protocol: "Rest"));
                services.AddSingleton<IProtoClientInitializer>(
                    new TestInitializer<TestClient>("API", _ => false, protocol: "GraphQL"));
                services.AddSingleton<IProtoClientInitializer>(fallback);
            });

        await using var host = builder.Build();
        await host.StartTestAsync("SharedFallbackNames", "00004", TestMethods.Placeholder);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(fallbackCalls, Is.EqualTo(1), "one invocation serves every chain that names the provider");
                Assert.That(Proto.Context.Client<TestClient>("api"), Is.Not.Null);
                Assert.That(Proto.Context.Client<TestClient>("API"), Is.Not.Null);
            });
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    private sealed class TestInitializer<TClient>(
        string name,
        Func<ProtoExecutionContext, bool> initialize,
        string? protocol = null) : IProtoClientInitializer<TClient>
        where TClient : class
    {
        public string Name { get; } = name;

        public string? Protocol { get; } = protocol;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
            => Task.FromResult(initialize(context));
    }

    private sealed class TestClient;
    private sealed class OtherTestClient;
}
