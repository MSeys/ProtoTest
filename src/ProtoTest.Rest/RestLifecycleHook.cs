namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Rest.Internal;
using System.Reflection;

internal class RestLifecycleHook : IProtoTestHook
{
    public int Order => 100;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var methodInfo = context.TestMethod;
        var classType = context.TestMethod.DeclaringType;

        if (methodInfo == null && classType == null)
            return Task.CompletedTask;

        var clientAttr = methodInfo?.GetCustomAttribute<RestClientAttribute>()
              ?? classType?.GetCustomAttribute<RestClientAttribute>();

        string? clientName = clientAttr?.ClientName;

        var authAttr = methodInfo?.GetCustomAttribute<RestAuthAttribute>()
                    ?? classType?.GetCustomAttribute<RestAuthAttribute>();

        context.SetContext(new RestContextState { Authenticator = authAttr?.CreateAuthenticator(), ClientName = clientName ?? "Default" });

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}