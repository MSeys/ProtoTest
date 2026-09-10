namespace ProtoTest.Rest.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

internal static class RestAuthenticatorFactory
{
    public static TAuthenticator Create<TAuthenticator>(
        ProtoExecutionContext context,
        object[] constructorArguments)
        where TAuthenticator : IRestAuthenticator
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
