namespace ProtoTest.Grpc;

using global::Grpc.Core;
using ProtoTest.Core;

/// <summary>
/// Per-client gRPC choices: metadata added to every call from the client, the hook that builds
/// per-call metadata, and the default deadline. Values layer from <c>ProtoTest:Grpc:Client</c> over the
/// code-based registration; the legacy <c>ProtoTest:Grpc</c> section still binds as a deprecated
/// fallback, so an existing suite keeps working.
/// </summary>
public sealed class GrpcClientOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Grpc:Client";

    /// <summary>The pre-1.1 section; it still binds, after <see cref="ConfigurationSectionName"/>. Deprecated.</summary>
    public const string LegacyConfigurationSectionName = "ProtoTest:Grpc";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    string? IProtoConfigurableOptions.FallbackConfigurationSectionName => LegacyConfigurationSectionName;

    /// <summary>Metadata added to every call from this client, keyed by its metadata name.</summary>
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds metadata per call with access to the test context - the hook for token auth.</summary>
    public Action<ProtoExecutionContext, Metadata>? ConfigureMetadata { get; set; }

    /// <summary>A default deadline applied when a call does not name one.</summary>
    public TimeSpan? DefaultDeadline { get; set; }

    /// <summary>
    /// Metadata keys whose values are redacted in the trace. Matching is case-insensitive and by
    /// substring, so <c>authorization</c> also covers <c>proxy-authorization</c>. The built-in test
    /// user's Base64 identity travels as <c>prototest-user</c> metadata and is redacted by default.
    /// Configuration under <c>ProtoTest:Grpc:Client:SensitiveMetadataKeys</c> extends these defaults.
    /// </summary>
    public List<string> SensitiveMetadataKeys { get; set; } =
        ["authorization", "cookie", "set-cookie", "x-api-key", "api-key", "token", "x-auth-token", "prototest-user"];

    /// <inheritdoc />
    public void Validate()
    {
        if (DefaultDeadline is { } deadline && deadline <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DefaultDeadline),
                deadline,
                "GrpcClientOptions.DefaultDeadline must be greater than zero when set.");
        }
    }
}
