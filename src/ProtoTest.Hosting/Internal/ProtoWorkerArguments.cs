namespace ProtoTest.Hosting.Internal;

/// <summary>
/// Composes the arguments the run hands a worker's entry point. The merged overlay travels as
/// <c>--{key}={value}</c> pairs so a <c>Host.CreateApplicationBuilder(args)</c> /
/// <c>Host.CreateDefaultBuilder(args)</c> entry point sees final-precedence values while its
/// <c>Main</c> runs, and <c>--contentRoot</c>/<c>--applicationName</c> keep the worker's own files and
/// identity. Those two names are reserved: an overlay key with either name is skipped so the
/// generated pair always wins. An entry point that ignores its arguments still receives the same
/// values at <c>HostBuilding</c> - that in-memory overlay is the fallback, not the primary path.
/// </summary>
internal static class ProtoWorkerArguments
{
    /// <summary>
    /// Builds the entry point's arguments: the worker's content root and application name first, then
    /// one command-line pair per overlay key. A key with a null value has no argument, and a key named
    /// <c>contentRoot</c>/<c>applicationName</c> (any casing) is skipped because the command line
    /// would otherwise carry a duplicate switch that wins by position over the generated pair.
    /// </summary>
    public static string[] Compose(
        string contentRoot,
        string applicationName,
        IReadOnlyDictionary<string, string?> configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(configuration);

        var arguments = new List<string>(configuration.Count + 4)
        {
            "--contentRoot",
            contentRoot,
            "--applicationName",
            applicationName
        };

        foreach (var (key, value) in configuration)
        {
            if (value is not null && !IsReservedIdentityKey(key))
            {
                arguments.Add($"--{key}={value}");
            }
        }

        return [.. arguments];
    }

    /// <summary>
    /// The two names the generated pair owns; command-line keys compare case-insensitively, so the
    /// check does too.
    /// </summary>
    private static bool IsReservedIdentityKey(string key) =>
        string.Equals(key, "contentRoot", StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, "applicationName", StringComparison.OrdinalIgnoreCase);
}
