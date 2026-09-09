namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

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
        await host.StartTestAsync("InitializerOrder", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

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
        await host.StartTestAsync("InitializerTypes", "00002", (MethodInfo)MethodInfo.GetCurrentMethod()!);

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

    private sealed class TestInitializer<TClient>(
        string name,
        Func<ProtoExecutionContext, bool> initialize) : IProtoClientInitializer<TClient>
        where TClient : class
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(
            ProtoExecutionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(initialize(context));
    }

    private sealed class TestClient;
    private sealed class OtherTestClient;
}
