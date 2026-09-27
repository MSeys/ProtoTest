namespace ProtoTest.AspNetCore.Internal;

/// <summary>
/// The typed state of one initialized in-process server. The trace entity state and the initialize
/// event both carry exactly these fields, so they can never disagree about what was started.
/// </summary>
internal sealed record AspNetCoreServerState(
    string ServerName,
    string ProgramType,
    string ProgramName,
    AspNetCoreServerLifetime Lifetime,
    bool Reused,
    bool WebHostCustomized,
    bool ClientCustomized,
    IReadOnlyList<string>? Substitutions = null)
{
    public string EntityId => $"server:{ProgramType}:{ServerName}";

    public string DisplayName => $"Server · {ProgramName}";

    public Dictionary<string, string?> ToAttributes()
    {
        var attributes = new Dictionary<string, string?>
        {
            ["aspnetcore.application.type"] = ProgramType,
            ["aspnetcore.server.lifetime"] = Lifetime.ToString(),
            ["aspnetcore.server.reused"] = Reused ? "true" : "false",
            ["aspnetcore.web_host.customized"] = WebHostCustomized ? "true" : "false",
            ["aspnetcore.client.customized"] = ClientCustomized ? "true" : "false"
        };
        if (Substitutions is { Count: > 0 })
        {
            attributes["aspnetcore.server.substituted"] = "true";
            attributes["aspnetcore.server.substitutions"] = string.Join(",", Substitutions);
        }

        return attributes;
    }
}
