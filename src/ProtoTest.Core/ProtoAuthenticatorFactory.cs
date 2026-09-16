namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Constructs a protocol authenticator so its constructor can request <see cref="ProtoExecutionContext"/>
/// directly, in addition to any services resolvable from DI and explicit constructor arguments.
/// </summary>
public static class ProtoAuthenticatorFactory
{
    public static TAuthenticator Create<TAuthenticator>(
        ProtoExecutionContext context,
        object[] constructorArguments)
        => ActivatorUtilities.CreateInstance<TAuthenticator>(
            new ContextServiceProvider(context),
            constructorArguments);

    private sealed class ContextServiceProvider(
        ProtoExecutionContext context) : IServiceProvider, IServiceProviderIsService
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(ProtoExecutionContext)
                ? context
                : context.Services.GetService(serviceType);

        public bool IsService(Type serviceType)
            => serviceType == typeof(ProtoExecutionContext)
               || context.Services.GetService<IServiceProviderIsService>()?.IsService(serviceType) == true;
    }
}
