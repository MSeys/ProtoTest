namespace ProtoTest.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Resolves the response and attachment options a protocol registered under its own key, so REST and
/// GraphQL never share configuration even though they use the same base option types. A protocol
/// integration resolves its own options through these methods, matching the key it registered with
/// <see cref="ProtoHttpOptionsRegistration"/>.
/// </summary>
public static class ProtoHttpOptionsResolver
{
    /// <summary>
    /// Resolves the response options registered for <paramref name="protocolName"/>, or the shared
    /// <c>ProtoTest:Http:Responses</c> defaults when the protocol registered none. The fallback resolves
    /// through <see cref="ProtoOptionsRegistration.Resolve{TOptions}(IServiceProvider, Func{TOptions}, Action{TOptions}?)"/>
    /// so the parameterless type's documented default section really binds.
    /// </summary>
    public static ProtoHttpResponseOptions ResolveResponseOptions(
        this ProtoExecutionContext context,
        string protocolName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        var registered = context.Services.GetKeyedService<ProtoHttpResponseOptions>(protocolName);
        if (registered is not null)
        {
            return registered;
        }

        // A hand-built execution context may have no configuration source; it gets the in-memory
        // defaults, while a host-provided context binds the shared default section.
        return context.Services.GetService<IConfiguration>() is null
            ? new ProtoHttpResponseOptions()
            : ProtoOptionsRegistration.Resolve(context.Services, () => new ProtoHttpResponseOptions());
    }

    /// <summary>
    /// Resolves the attachment options registered for <paramref name="protocolName"/>, or
    /// <see langword="null"/> when the protocol has not opted into attachment capture. Absence is the
    /// opt-in signal, so no default instance is invented here; the parameterless type's
    /// <c>ProtoTest:Http:Attachments</c> section binds wherever the type is resolved through
    /// <see cref="ProtoOptionsRegistration"/>.
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

