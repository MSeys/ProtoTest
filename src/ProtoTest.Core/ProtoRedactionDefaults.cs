namespace ProtoTest.Core;

/// <summary>
/// The names redacted by default wherever ProtoTest writes JSON diagnostics: attachment bodies,
/// observation data and traced state values all read this one list, so no axis can hide more than
/// another.
/// </summary>
public static class ProtoRedactionDefaults
{
    /// <summary>Property names whose values are replaced with <c>[REDACTED]</c> by default.</summary>
    public static IReadOnlyList<string> SensitivePropertyNames { get; } =
    [
        "password",
        "token",
        "access_token",
        "refresh_token",
        "secret",
        "apiKey",
        "api_key",
        "authorization",
        "cookie",
        "connectionString",
        "clientSecret"
    ];
}
