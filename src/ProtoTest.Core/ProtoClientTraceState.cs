namespace ProtoTest.Core;

/// <summary>
/// The client state every initializer records once, so a client's configuration reads the same in the
/// trace whichever protocol created it: name, protocol, concrete type, where the endpoint came from,
/// and the protocol's own details.
/// </summary>
public static class ProtoClientTraceState
{
    public static void SetConfiguration(
        ProtoExecutionContext context,
        Type clientType,
        string protocolName,
        string name,
        string scopedName,
        string label,
        string source,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clientType);
        var state = new Dictionary<string, string?>
        {
            ["client.name"] = name,
            ["client.protocol"] = protocolName,
            ["client.type"] = clientType.FullName,
            ["client.endpoint_source"] = source
        };
        if (details is not null)
        {
            foreach (var (key, value) in details)
            {
                state[key] = value;
            }
        }

        context.Trace.SetEntityState(
            ProtoTraceEntityKinds.Client,
            ProtoClientTrace.Id(clientType, scopedName),
            label,
            state,
            scope: context.TestName);
    }
}
