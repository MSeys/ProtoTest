namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Applies an <see cref="IProtoHttpAuthenticator"/> to the HTTP-based clients of a test, for example
/// <c>[Auth&lt;BearerTokenAuthenticator&gt;("token")]</c>. Several attributes at the same level are
/// ordered by <see cref="Order"/> and composed.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthAttribute<TAuthenticator>(params object[] constructorArgs)
    : Attribute, IProtoHttpAuthMetadata
    where TAuthenticator : class, IProtoHttpAuthenticator
{
    public int Order { get; init; }

    /// <summary>
    /// Restricts the authenticator to the named protocols, for example <c>["GraphQL"]</c>. When empty
    /// the authenticator applies to every HTTP-based protocol the application exposes.
    /// </summary>
    public string[] Protocols { get; init; } = [];

    IReadOnlyList<string> IProtoHttpAuthMetadata.Protocols => Protocols;

    public bool AppliesTo(string protocolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        return Protocols.Length == 0
            || Protocols.Contains(protocolName, StringComparer.OrdinalIgnoreCase);
    }

    IProtoHttpAuthenticator IProtoHttpAuthMetadata.Create(ProtoExecutionContext context)
        => ProtoAuthenticatorFactory.Create<TAuthenticator>(context, constructorArgs);
}
