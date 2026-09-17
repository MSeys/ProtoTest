namespace ProtoTest.Core;

/// <summary>The application selected for the current test and any per-protocol client bindings.</summary>
public sealed class ProtoApplicationState : IProtoContext
{
    public ProtoApplicationState(string applicationName, IReadOnlyDictionary<string, string> bindings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ApplicationName = applicationName;
        Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    /// <summary>Gets the selected application name.</summary>
    public string ApplicationName { get; }

    /// <summary>Gets the protocol-to-client bindings declared by <c>[Application]</c>.</summary>
    public IReadOnlyDictionary<string, string> Bindings { get; }

    /// <summary>Returns the client bound to a protocol by <c>[Application]</c>, or <see langword="null"/>.</summary>
    public string? Client(string protocolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        return Bindings.TryGetValue(protocolName, out var client) ? client : null;
    }
}
