namespace ProtoTest.Grpc;

using global::Grpc.Core;
using ProtoTest.Core;

/// <summary>
/// Per-client gRPC choices: metadata added to every call from the client, the hook that builds
/// per-call metadata, and the default deadline. Values layer from <c>ProtoTest:Grpc</c> over the
/// code-based registration.
/// </summary>
public sealed class GrpcClientOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Grpc";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Metadata added to every call from this client, keyed by its metadata name.</summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds metadata per call with access to the test context - the hook for token auth.</summary>
    public Action<ProtoExecutionContext, Metadata>? ConfigureMetadata { get; set; }

    /// <summary>A default deadline applied when a call does not name one.</summary>
    public TimeSpan? DefaultDeadline { get; set; }

    /// <summary>
    /// Metadata keys whose values are redacted in the trace. Matching is case-insensitive and by
    /// substring, so <c>authorization</c> also covers <c>proxy-authorization</c>. Configuration
    /// under <c>ProtoTest:Grpc:SensitiveMetadataKeys</c> extends these defaults.
    /// </summary>
    public List<string> SensitiveMetadataKeys { get; set; } =
        ["authorization", "cookie", "set-cookie", "x-api-key", "api-key", "token", "x-auth-token"];
}
