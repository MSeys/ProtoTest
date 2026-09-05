namespace ProtoTest.Rest;

using ProtoTest.Core;
using ProtoTest.Rest.Authenticators;
using ProtoTest.Rest.Internal;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public abstract class RestAuthAttribute : Attribute
{
    public abstract IRestAuthenticator CreateAuthenticator();

    public void ApplyToContext(ProtoExecutionContext context, string? clientName)
    {
        var authenticator = CreateAuthenticator();
        context.SetContext(new RestContextState { Authenticator = authenticator, ClientName = clientName ?? "Default" });
    }
}

/// <summary>
/// Generic attribute to apply an IRestAuthenticator directly to a test method or class.
/// Usage: [Auth&lt;MyAuthenticator&gt;] or [Auth&lt;BearerTokenAuth&gt;("secret-token")]
/// </summary>
public class AuthAttribute<TAuth>(params object[] constructorArgs) : RestAuthAttribute
    where TAuth : IRestAuthenticator
{
    public override IRestAuthenticator CreateAuthenticator()
    {
        if (constructorArgs.Length > 0)
        {
            return (IRestAuthenticator)Activator.CreateInstance(typeof(TAuth), constructorArgs)!;
        }

        return Activator.CreateInstance<TAuth>()!;
    }
}

/// <summary>
/// Shortcut attribute for Bearer Token authentication.
/// Usage: [BearerToken("your-jwt-token")]
/// </summary>
public class BearerTokenAttribute(string token) : RestAuthAttribute
{
    public override IRestAuthenticator CreateAuthenticator()
        => new BearerTokenAuthenticator(token);
}

/// <summary>
/// Shortcut attribute for API Key authentication.
/// Usage: [ApiKey("X-API-Key", "your-api-key")]
/// </summary>
public class ApiKeyAttribute(string key, string value, ApiKeyLocation location = ApiKeyLocation.Header) : RestAuthAttribute
{
    public override IRestAuthenticator CreateAuthenticator()
        => new ApiKeyAuthenticator(key, value, location);
}

/// <summary>
/// Shortcut attribute for Basic Authentication.
/// Usage: [BasicAuth("username", "password")]
/// </summary>
public class BasicAuthAttribute(string username, string password) : RestAuthAttribute
{
    public override IRestAuthenticator CreateAuthenticator()
        => new BasicAuthAuthenticator(username, password);
}