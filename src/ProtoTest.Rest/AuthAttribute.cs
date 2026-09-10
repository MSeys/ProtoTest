namespace ProtoTest.Rest;

using ProtoTest.Core;

internal interface IRestAuthMetadata
{
    int Order { get; }
    IRestAuthenticator CreateAuthenticator(ProtoExecutionContext context);
}

/// <summary>
/// Generic attribute to apply an IRestAuthenticator directly to a test method or class.
/// Usage: [Auth&lt;MyAuthenticator&gt;] or [Auth&lt;BearerTokenAuthenticator&gt;("secret-token")]
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class AuthAttribute<TAuth>(params object[] constructorArgs) : Attribute, IRestAuthMetadata
    where TAuth : IRestAuthenticator
{
    public int Order { get; init; }

    IRestAuthenticator IRestAuthMetadata.CreateAuthenticator(ProtoExecutionContext context)
        => Internal.RestAuthenticatorFactory.Create<TAuth>(context, constructorArgs);
}
