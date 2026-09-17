namespace ProtoTest.Core;

/// <summary>
/// Resolves the clients registered for each application. Integrations use it to pick a protocol's
/// default client — the first registered — when a test does not name one explicitly.
/// </summary>
public sealed class ProtoApplicationRegistry
{
    private readonly Dictionary<string, Dictionary<string, List<string>>> _byApplication;

    public ProtoApplicationRegistry(IEnumerable<ProtoApplicationClients> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        _byApplication = new(StringComparer.OrdinalIgnoreCase);

        foreach (var application in applications)
        {
            if (!_byApplication.TryGetValue(application.ApplicationName, out var protocols))
            {
                protocols = new(StringComparer.OrdinalIgnoreCase);
                _byApplication[application.ApplicationName] = protocols;
            }

            foreach (var client in application.Clients)
            {
                if (!protocols.TryGetValue(client.ProtocolName, out var names))
                {
                    names = [];
                    protocols[client.ProtocolName] = names;
                }

                if (!names.Contains(client.ClientName, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(client.ClientName);
                }
            }
        }
    }

    /// <summary>Returns the client names registered for a protocol, in registration order.</summary>
    public IReadOnlyList<string> Clients(string applicationName, string protocolName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        return _byApplication.TryGetValue(applicationName, out var protocols)
               && protocols.TryGetValue(protocolName, out var names)
            ? [.. names]
            : [];
    }

    /// <summary>Returns the first client registered for a protocol, or <see langword="null"/>.</summary>
    public string? DefaultClient(string applicationName, string protocolName)
        => Clients(applicationName, protocolName).FirstOrDefault();
}
