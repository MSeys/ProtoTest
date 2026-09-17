namespace ProtoTest.Http;

using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// Resolves the ordered authenticator attributes for a protocol before each test, builds a composite
/// authenticator when more than one applies, and records a "&lt;protocol&gt;.context.configure" trace
/// event. The client a test uses is chosen separately by <c>[Application]</c>.
/// </summary>
public abstract class ProtoHttpAuthLifecycleHook<TAuthMetadata>(string protocolName)
    : IProtoTestHook
    where TAuthMetadata : class, IProtoHttpAuthMetadata
{
    private readonly string _protocolName = string.IsNullOrWhiteSpace(protocolName)
        ? throw new ArgumentException("A protocol name is required.", nameof(protocolName))
        : protocolName;

    public int Order => 100;

    /// <summary>Stores the resolved authenticator factory as protocol-specific context state.</summary>
    protected abstract void SetContext(
        ProtoExecutionContext context,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory);

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var method = context.TestMethod;
        var classType = method.DeclaringType;
        var methodAuth = method.GetCustomAttributes(inherit: true).OfType<TAuthMetadata>().ToArray();
        var classAuth = classType?.GetCustomAttributes(inherit: true).OfType<TAuthMetadata>().ToArray() ?? [];
        var auth = methodAuth.Length > 0 ? methodAuth : classAuth;
        var orderedAuth = auth.OrderBy(attribute => attribute.Order).ToArray();

        SetContext(context, orderedAuth.Length == 0
            ? null
            : executionContext => CreateAuthenticator(orderedAuth, executionContext));

        context.Trace.WriteEvent(
            $"{_protocolName.ToLowerInvariant()}.context.configure",
            $"{_protocolName} auth",
            $"ProtoTest.{_protocolName}",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            new Dictionary<string, string?>
            {
                ["auth.source"] = methodAuth.Length > 0 ? "method" : classAuth.Length > 0 ? "class" : "none",
                ["auth.count"] = orderedAuth.Length.ToString(),
                ["auth.types"] = string.Join(", ", orderedAuth.Select(AuthTypeName))
            });

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private IProtoHttpAuthenticator CreateAuthenticator(
        IReadOnlyList<TAuthMetadata> attributes,
        ProtoExecutionContext context)
    {
        var authenticators = attributes.Select(attribute => attribute.Create(context)).ToArray();
        return authenticators.Length == 1
            ? authenticators[0]
            : new ProtoCompositeHttpAuthenticator(authenticators, $"ProtoTest.{_protocolName}");
    }

    private static string AuthTypeName(TAuthMetadata metadata)
        => metadata.GetType().GenericTypeArguments.FirstOrDefault()?.FullName
           ?? metadata.GetType().FullName
           ?? metadata.GetType().Name;
}
