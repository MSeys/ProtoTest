namespace ProtoTest.Grpc.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;

/// <summary>
/// Pins the per-client contract of <c>AddClient</c>'s <c>configure</c> callback: the options belong to
/// the named client, so metadata, deadline and sensitive keys never leak across clients, a
/// repeated registration for the same name still composes its callbacks in order, and the shared
/// <c>ProtoTest:Grpc:Client</c> section still binds over each client's code callback.
/// </summary>
[TestFixture]
public sealed class GrpcClientOptionsIsolationTests
{
    [Test]
    [NonParallelizable]
    public async Task TwoClients_ShouldKeepTheirMetadataDeadlineAndSensitiveKeysApart()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc =>
        {
            grpc.AddClient("Alpha", GrpcTestServer.Address, options =>
            {
                options.Metadata["x-alpha"] = "alpha-value";
                options.Metadata["x-shared"] = "alpha";
                options.DefaultDeadline = TimeSpan.FromSeconds(30);
                options.SensitiveMetadataKeys.Add("x-alpha-secret");
            });
            grpc.AddClient("Beta", GrpcTestServer.Address, options =>
            {
                options.Metadata["x-beta"] = "beta-value";
                options.Metadata["x-shared"] = "beta";
                options.DefaultDeadline = TimeSpan.FromSeconds(60);
            });
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc client options isolation", TestMethods.Placeholder);

        var started = DateTime.UtcNow;
        await context.Grpc("Alpha").UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "isolation-alpha" },
            metadata => metadata.Add("x-alpha-secret", "alpha-secret"));
        await context.Grpc("Beta").UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "isolation-beta" },
            metadata => metadata.Add("x-beta-secret", "beta-secret"));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var calls = host.Trace.Snapshot().Tests.Single().Entries
            .Where(entry => entry.Kind == "grpc.call")
            .ToArray();
        var alpha = calls.Single(entry => entry.Attributes["client.name"] == "Alpha");
        var beta = calls.Single(entry => entry.Attributes["client.name"] == "Beta");

        Assert.Multiple(() =>
        {
            Assert.That(alpha.Attributes["rpc.metadata.x-alpha"], Is.EqualTo("alpha-value"));
            Assert.That(alpha.Attributes["rpc.metadata.x-shared"], Is.EqualTo("alpha"),
                "a later client's metadata does not overwrite this client's value");
            Assert.That(alpha.Attributes.ContainsKey("rpc.metadata.x-beta"), Is.False,
                "another client's metadata does not leak into this client");
            Assert.That(beta.Attributes["rpc.metadata.x-beta"], Is.EqualTo("beta-value"));
            Assert.That(beta.Attributes["rpc.metadata.x-shared"], Is.EqualTo("beta"));
            Assert.That(beta.Attributes.ContainsKey("rpc.metadata.x-alpha"), Is.False);

            Assert.That(alpha.Attributes["rpc.metadata.x-alpha-secret"], Is.EqualTo("(redacted)"));
            Assert.That(beta.Attributes["rpc.metadata.x-beta-secret"], Is.EqualTo("beta-secret"),
                "another client's sensitive keys do not leak into this client's redaction");

            AssertDeadline(EchoService.Deadlines["isolation-alpha"], started + TimeSpan.FromSeconds(30));
            AssertDeadline(EchoService.Deadlines["isolation-beta"], started + TimeSpan.FromSeconds(60));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task RepeatedRegistration_ShouldComposeBothCallbacksInOrder()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Compose", GrpcTestServer.Address, options =>
        {
            options.Metadata["x-first"] = "first";
            options.Metadata["x-shared"] = "first";
            options.DefaultDeadline = TimeSpan.FromSeconds(30);
        }));
        builder.AddGrpc(grpc => grpc.AddClient("Compose", GrpcTestServer.Address, options =>
        {
            options.Metadata["x-second"] = "second";
            options.Metadata["x-shared"] = "second";
            options.DefaultDeadline = TimeSpan.FromSeconds(60);
        }));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc repeated client registration", TestMethods.Placeholder);

        var started = DateTime.UtcNow;
        await context.Grpc("Compose").UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "repeated-compose" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(call.Attributes["rpc.metadata.x-first"], Is.EqualTo("first"),
                "the first registration's distinct key still applies");
            Assert.That(call.Attributes["rpc.metadata.x-second"], Is.EqualTo("second"),
                "the second registration's distinct key also applies");
            Assert.That(call.Attributes["rpc.metadata.x-shared"], Is.EqualTo("second"),
                "the later registration overrides the earlier value for the same key");
            AssertDeadline(EchoService.Deadlines["repeated-compose"], started + TimeSpan.FromSeconds(60));
        });
    }

    [Test]
    [NonParallelizable]
    public async Task ClientOptions_ShouldBindTheSharedSectionOverTheCodeCallback()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Grpc:Client:DefaultDeadline"] = "00:00:20",
                ["ProtoTest:Grpc:Client:Metadata:x-config"] = "configured"
            }));
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address, options =>
        {
            options.Metadata.Add("x-code", "code");
            options.DefaultDeadline = TimeSpan.FromSeconds(90);
        }));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc client options layering", TestMethods.Placeholder);

        var started = DateTime.UtcNow;
        await context.Grpc("Echo").UnaryAsync(EchoMethods.Say, new EchoRequest { Message = "layering" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(call.Attributes["rpc.metadata.x-code"], Is.EqualTo("code"),
                "the client's code callback still applies");
            Assert.That(call.Attributes["rpc.metadata.x-config"], Is.EqualTo("configured"),
                "the shared section binds into the named client's options");
            AssertDeadline(EchoService.Deadlines["layering"], started + TimeSpan.FromSeconds(20));
        });
    }

    private static void AssertDeadline(DateTime actual, DateTime expected)
        => Assert.That(
            actual,
            Is.InRange(expected - TimeSpan.FromSeconds(5), expected + TimeSpan.FromSeconds(5)),
            "the call used this client's own default deadline");
}
