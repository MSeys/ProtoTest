namespace ProtoTest.GraphQL.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using System.Net.Http;
using System.Reflection;

[TestFixture]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddGraphQL_CalledTwice_ShouldComposeClientsAndRegisterInfrastructureOnce()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddGraphQL(graphql => graphql.AddClient("ControlPlane", "https://api.test/graphql"));
        builder.AddGraphQL(graphql => graphql.AddClient("Other", "https://other.test/graphql"));
        builder.AddGraphQL();

        Assert.Multiple(() =>
        {
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(2));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IGraphQLWebSocketFactory)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoHttpResponseOptions)
                    && descriptor.IsKeyedService
                    && Equals(descriptor.ServiceKey, ProtoGraphQLBuilder.ProtocolName)),
                Is.EqualTo(1));
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("graphql idempotent", TestMethod());

        Assert.Multiple(() =>
        {
            Assert.That(
                context.Client<HttpClient>("ControlPlane").BaseAddress,
                Is.EqualTo(new Uri("https://api.test/graphql")));
            Assert.That(
                context.Client<HttpClient>("Other").BaseAddress,
                Is.EqualTo(new Uri("https://other.test/graphql")));
        });
        _ = context.GraphQL("ControlPlane");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public void AddGraphQL_WhenConfigureThrows_ShouldNotPreventALaterSuccessfulCall()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);

        Assert.Throws<InvalidOperationException>(() =>
            builder.AddGraphQL(_ => throw new InvalidOperationException("configure exploded")));
        builder.AddGraphQL(graphql => graphql.AddClient("ControlPlane", "https://api.test/graphql"));

        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoTestHook)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoClientInitializer)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(IGraphQLWebSocketFactory)),
                Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ConfigureResponsesAndCaptureAttachments_CalledTwice_ShouldComposeBothCallbacks()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGraphQL(graphql =>
        {
            graphql.ConfigureResponses(options => options.MaxResponseBodyBytes = 4096);
            graphql.CaptureAttachments(options => options.CaptureResponses = false);
        });
        builder.AddGraphQL(graphql =>
        {
            graphql.ConfigureResponses(options => options.MaxDiagnosticBodyLength = 64);
            graphql.CaptureAttachments(options => options.MaxDiagnosticBodyLength = 128);
        });

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("graphql options compose", TestMethod());

        var responses = context.ResolveResponseOptions(ProtoGraphQLBuilder.ProtocolName);
        var attachments = context.ResolveAttachmentOptions(ProtoGraphQLBuilder.ProtocolName);

        Assert.Multiple(() =>
        {
            Assert.That(responses.MaxResponseBodyBytes, Is.EqualTo(4096));
            Assert.That(responses.MaxDiagnosticBodyLength, Is.EqualTo(64));
            Assert.That(attachments, Is.Not.Null);
            Assert.That(attachments!.CaptureResponses, Is.False);
            Assert.That(attachments.MaxDiagnosticBodyLength, Is.EqualTo(128));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    private static MethodInfo TestMethod()
        => typeof(RegistrationIdempotencyTests).GetMethod(
            nameof(Placeholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void Placeholder()
    {
    }
}
