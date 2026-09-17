namespace ProtoTest.Http.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using System.Reflection;

[TestFixture]
public class AuthAttributeTests
{
    [Test]
    public async Task BeforeTestAsync_ShouldIgnoreAuthenticatorsScopedToOtherProtocols()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var rest = new CaptureHook("Rest");
        var graphql = new CaptureHook("GraphQL");
        var context = new ProtoExecutionContext("Test", scope, "00001", Method(nameof(GraphQLOnly)));

        // Act
        await rest.BeforeTestAsync(context);
        await graphql.BeforeTestAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rest.Factory, Is.Null);
            Assert.That(graphql.Factory, Is.Not.Null);
        }
    }

    [Test]
    public async Task BeforeTestAsync_ShouldApplyUnscopedAuthenticatorsToEveryProtocol()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var rest = new CaptureHook("Rest");
        var graphql = new CaptureHook("GraphQL");
        var context = new ProtoExecutionContext("Test", scope, "00001", Method(nameof(EveryProtocol)));

        // Act
        await rest.BeforeTestAsync(context);
        await graphql.BeforeTestAsync(context);

        // Assert
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rest.Factory, Is.Not.Null);
            Assert.That(graphql.Factory, Is.Not.Null);
        }
    }

    [Test]
    public async Task BeforeTestAsync_ShouldPreferMethodAttributesOverClassAttributes()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var hook = new CaptureHook("Rest");
        var context = new ProtoExecutionContext("Test", scope, "00001", Method(nameof(EveryProtocol), typeof(MethodOverrideCases)));

        // Act
        await hook.BeforeTestAsync(context);

        // Assert: the method attribute replaces the class-level one rather than merging.
        Assert.That(hook.Factory, Is.Not.Null);
        var authenticator = hook.Factory!(context);
        Assert.That(authenticator, Is.TypeOf<RecordingAuthenticator>());
    }

    private static MethodInfo Method(string name, Type? declaring = null)
        => (declaring ?? typeof(AuthAttributeTests))
            .GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
        ?? throw new InvalidOperationException($"Method '{name}' was not found.");

    [Auth<RecordingAuthenticator>(Protocols = ["GraphQL"])]
    private void GraphQLOnly()
    {
    }

    [Auth<RecordingAuthenticator>]
    private void EveryProtocol()
    {
    }

    [Auth<RecordingAuthenticator>]
    private sealed class MethodOverrideCases
    {
        [Auth<RecordingAuthenticator>]
        public void EveryProtocol()
        {
        }
    }

    private sealed class CaptureHook(string protocolName) : ProtoHttpAuthLifecycleHook(protocolName)
    {
        public Func<ProtoExecutionContext, IProtoHttpAuthenticator>? Factory { get; private set; }

        protected override void SetContext(
            ProtoExecutionContext context,
            Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory)
            => Factory = authenticatorFactory;
    }

    public sealed class RecordingAuthenticator : IProtoHttpAuthenticator
    {
        public ValueTask AuthenticateAsync(
            ProtoHttpAuthenticationContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }
}
