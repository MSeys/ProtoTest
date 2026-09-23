namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>Shared resolution of the base address an HTTP-based client sends to.</summary>
public static class ProtoHttpEndpoint
{
    /// <summary>
    /// Resolves the client's base address, from the per-test resolver when there is one and from the
    /// client otherwise, and validates that it is an absolute HTTP or HTTPS address.
    /// </summary>
    public static async ValueTask<Uri> ResolveBaseAddressAsync(
        HttpClient client,
        string protocolName,
        string targetName,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>>? resolver,
        ProtoExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        var endpoint = resolver is not null
            ? await resolver(context, cancellationToken)
            : client.BaseAddress
              ?? throw new InvalidOperationException($"{protocolName} client '{targetName}' has no endpoint.");
        if (!endpoint.IsAbsoluteUri || !ProtoHttpUri.IsHttpUri(endpoint))
        {
            throw new InvalidOperationException(
                $"A per-test {protocolName} endpoint must be an absolute HTTP or HTTPS URI.");
        }

        return endpoint;
    }
}
