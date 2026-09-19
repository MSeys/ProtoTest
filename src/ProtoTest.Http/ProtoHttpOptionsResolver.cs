namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Resolves the response and attachment options a protocol registered under its own key, so REST and
/// GraphQL never share configuration even though they use the same base option types.
/// </summary>
public static class ProtoHttpOptionsResolver
{
    /// <summary>
    /// Resolves the response options registered for <paramref name="protocolName"/>, or defaults when
    /// the protocol registered none.
    /// </summary>
    public static ProtoHttpResponseOptions ResolveResponseOptions(
        this ProtoExecutionContext context,
        string protocolName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        return context.Services.GetKeyedService<ProtoHttpResponseOptions>(protocolName)
            ?? new ProtoHttpResponseOptions();
    }

    /// <summary>
    /// Resolves the attachment options registered for <paramref name="protocolName"/>, or
    /// <see langword="null"/> when the protocol has not opted into attachment capture.
    /// </summary>
    public static ProtoHttpAttachmentOptions? ResolveAttachmentOptions(
        this ProtoExecutionContext context,
        string protocolName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        return context.Services.GetKeyedService<ProtoHttpAttachmentOptions>(protocolName);
    }
}
