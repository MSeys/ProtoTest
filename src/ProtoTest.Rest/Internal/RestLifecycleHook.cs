namespace ProtoTest.Rest.Internal;

using System.Reflection;
using ProtoTest.Core;

internal sealed class RestLifecycleHook : IProtoTestHook
{
    public int Order => 100;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var methodInfo = context.TestMethod;
        var classType = context.TestMethod.DeclaringType;

        var clientAttribute = methodInfo.GetCustomAttribute<RestClientAttribute>()
            ?? classType?.GetCustomAttribute<RestClientAttribute>();

        var methodAuthAttributes = methodInfo.GetCustomAttributes(inherit: true)
            .OfType<IRestAuthMetadata>()
            .ToArray();
        var authAttributes = methodAuthAttributes.Length > 0
            ? methodAuthAttributes
            : classType?.GetCustomAttributes(inherit: true)
                .OfType<IRestAuthMetadata>()
                .ToArray() ?? [];
        var orderedAuthAttributes = authAttributes
            .OrderBy(attribute => attribute.Order)
            .ToArray();

        context.SetContext(new RestContextState
        {
            AuthenticatorFactory = orderedAuthAttributes.Length == 0
                ? null
                : executionContext => CreateAuthenticator(orderedAuthAttributes, executionContext),
            ClientName = clientAttribute?.ClientName ?? "Default"
        });

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private static IRestAuthenticator CreateAuthenticator(
        IReadOnlyList<IRestAuthMetadata> attributes,
        ProtoExecutionContext context)
    {
        var authenticators = attributes
            .Select(attribute => attribute.CreateAuthenticator(context))
            .ToArray();
        return authenticators.Length == 1
            ? authenticators[0]
            : new CompositeRestAuthenticator(authenticators);
    }
}
