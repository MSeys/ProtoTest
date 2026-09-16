namespace ProtoTest.Rest;

using ProtoTest.Http;

internal interface IRestAuthMetadata : IProtoHttpAuthMetadata;

/// <summary>
/// Generic attribute to apply an IProtoHttpAuthenticator directly to a test method or class.
/// Usage: [Auth&lt;MyAuthenticator&gt;] or [Auth&lt;BearerTokenAuthenticator&gt;("secret-token")]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthAttribute<TAuthenticator>(params object[] constructorArgs)
    : ProtoHttpAuthAttribute<TAuthenticator>(constructorArgs), IRestAuthMetadata
    where TAuthenticator : class, IProtoHttpAuthenticator;
