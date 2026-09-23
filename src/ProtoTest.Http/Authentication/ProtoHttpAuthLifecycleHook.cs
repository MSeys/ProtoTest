namespace ProtoTest.Http;

using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// Resolves the ordered authenticator attributes that apply to a protocol before each test, builds a
/// composite authenticator when more than one applies, and records the resolved configuration as the
/// protocol's auth entity state. The client a test uses is chosen separately by <c>[Application]</c>.
/// The state is stored under the protocol's key, so protocols sharing this hook never overwrite each
/// other.
/// </summary>
public sealed class ProtoHttpAuthLifecycleHook(ProtoProtocol protocol) : IProtoTestHook
{
    private readonly ProtoProtocol _protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));

    public int Order => ProtoHookOrder.Authentication;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var method = context.TestMethod;
        var methodAuth = Applicable(method.GetCustomAttributes(inherit: true)).ToArray();
        // A test method inherited from a base class is reflected on the test class that runs it, so the
        // class-level [Auth] attributes are read from the reflected type; inherit:true walks the base
        // hierarchy, so attributes declared on a base test class are found too.
        var classType = method.ReflectedType is { } reflected
            && (method.DeclaringType is null || method.DeclaringType.IsAssignableFrom(reflected))
                ? reflected
                : method.DeclaringType;
        var classAuth = classType is null ? [] : Applicable(classType.GetCustomAttributes(inherit: true)).ToArray();
        var auth = methodAuth.Length > 0 ? methodAuth : classAuth;
        var orderedAuth = auth.OrderBy(attribute => attribute.Order).ToArray();

        context.SetContext(
            _protocol.Key,
            new ProtoHttpContextState
            {
                AuthenticatorFactory = orderedAuth.Length == 0
                    ? null
                    : executionContext => CreateAuthenticator(orderedAuth, executionContext)
            });

        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Auth,
            _protocol.Key,
            $"{_protocol.Key} authentication",
            new Dictionary<string, string?>
            {
                ["auth.source"] = methodAuth.Length > 0 ? "method" : classAuth.Length > 0 ? "class" : "none",
                ["auth.count"] = orderedAuth.Length.ToString(),
                ["auth.types"] = string.Join(", ", orderedAuth.Select(AuthTypeName))
            },
            scope: context.TestName,
            change: "configured");

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private IEnumerable<IProtoHttpAuthMetadata> Applicable(IEnumerable<object> attributes)
        => attributes
            .OfType<IProtoHttpAuthMetadata>()
            .Where(metadata => metadata.AppliesTo(_protocol.Key));

    private IProtoHttpAuthenticator CreateAuthenticator(
        IReadOnlyList<IProtoHttpAuthMetadata> attributes,
        ProtoExecutionContext context)
    {
        var authenticators = attributes.Select(attribute => attribute.Create(context)).ToArray();
        return authenticators.Length == 1
            ? authenticators[0]
            : new ProtoCompositeHttpAuthenticator(authenticators, _protocol.TraceSource);
    }

    private static string AuthTypeName(IProtoHttpAuthMetadata metadata)
        => metadata.GetType().GenericTypeArguments.FirstOrDefault()?.FullName
           ?? metadata.GetType().FullName
           ?? metadata.GetType().Name;
}
