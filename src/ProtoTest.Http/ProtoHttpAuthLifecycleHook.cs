namespace ProtoTest.Http;

using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// Resolves a named-client attribute and ordered authenticator attributes for a protocol integration
/// before each test, builds a composite authenticator when more than one applies, and records a
/// "&lt;protocol&gt;.context.configure" trace event describing what was resolved.
/// </summary>
public abstract class ProtoHttpAuthLifecycleHook<TClientAttribute, TAuthMetadata>(string protocolName)
    : IProtoTestHook
    where TClientAttribute : Attribute
    where TAuthMetadata : class, IProtoHttpAuthMetadata
{
    private readonly string _protocolName = string.IsNullOrWhiteSpace(protocolName)
        ? throw new ArgumentException("A protocol name is required.", nameof(protocolName))
        : protocolName;

    public int Order => 100;

    /// <summary>Reads the client name off the resolved client attribute, or "Default" when none was applied.</summary>
    protected abstract string GetClientName(TClientAttribute? attribute);

    /// <summary>Stores the resolved client name and authenticator factory as protocol-specific context state.</summary>
    protected abstract void SetContext(
        ProtoExecutionContext context,
        string clientName,
        Func<ProtoExecutionContext, IProtoHttpAuthenticator>? authenticatorFactory);

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var method = context.TestMethod;
        var classType = method.DeclaringType;
        var clientAttribute = method.GetCustomAttribute<TClientAttribute>()
            ?? classType?.GetCustomAttribute<TClientAttribute>();

        var methodAuth = method.GetCustomAttributes(inherit: true).OfType<TAuthMetadata>().ToArray();
        var classAuth = classType?.GetCustomAttributes(inherit: true).OfType<TAuthMetadata>().ToArray() ?? [];
        var auth = methodAuth.Length > 0 ? methodAuth : classAuth;
        var orderedAuth = auth.OrderBy(attribute => attribute.Order).ToArray();
        var clientName = GetClientName(clientAttribute);

        SetContext(context, clientName, orderedAuth.Length == 0
            ? null
            : executionContext => CreateAuthenticator(orderedAuth, executionContext));

        context.Trace.WriteEvent(
            $"{_protocolName.ToLowerInvariant()}.context.configure",
            $"{_protocolName} context · {clientName}",
            $"ProtoTest.{_protocolName}",
            ProtoTracePhase.Setup,
            ProtoTraceOutcome.Succeeded,
            new Dictionary<string, string?>
            {
                ["client.name"] = clientName,
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
