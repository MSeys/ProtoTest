namespace ProtoTest.Rest;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RestClientAttribute : Attribute
{
    public string ClientName { get; }

    public RestClientAttribute(string clientName)
    {
        ArgumentException.ThrowIfNullOrEmpty(clientName);
        ClientName = clientName;
    }
}