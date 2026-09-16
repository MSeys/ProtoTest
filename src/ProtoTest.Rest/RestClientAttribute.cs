namespace ProtoTest.Rest;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RestClientAttribute(string clientName) : Attribute
{
    public string ClientName { get; } = string.IsNullOrEmpty(clientName)
        ? throw new ArgumentException("A REST client name is required.", nameof(clientName))
        : clientName;
}