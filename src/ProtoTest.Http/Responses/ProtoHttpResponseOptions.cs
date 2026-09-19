namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Controls how HTTP response bodies are buffered, shared by the REST and GraphQL integrations.
/// Each protocol registers an instance created with its own configuration section.
/// </summary>
public class ProtoHttpResponseOptions : IProtoConfigurableOptions
{
    /// <summary>Creates options for a protocol that does not name a section, defaulting to <c>ProtoTest:Http:Responses</c>.</summary>
    public ProtoHttpResponseOptions()
        : this("ProtoTest:Http:Responses")
    {
    }

    /// <summary>Creates options bound to a known configuration section, e.g. <c>ProtoTest:Rest:Responses</c>.</summary>
    public ProtoHttpResponseOptions(string configurationSectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        ConfigurationSectionName = configurationSectionName;
    }

    /// <summary>Maximum response body size buffered by a convenience API. Defaults to 10 MiB.</summary>
    public int MaxResponseBodyBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Maximum length of the response body embedded in a failure message, such as the body in a
    /// <c>RestStatusAssertionException</c>. Defaults to 64 KiB and binds from the same section, so a
    /// protocol's diagnostic cap can be configured alongside its buffering cap.
    /// </summary>
    public int MaxDiagnosticBodyLength { get; set; } = 64 * 1024;

    /// <summary>The configuration section this instance binds from, e.g. "ProtoTest:Rest:Responses".</summary>
    public string ConfigurationSectionName { get; }
}
