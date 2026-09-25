namespace ProtoTest.Hosting.Internal;

/// <summary>
/// Composes the arguments the run hands a worker's entry point. The merged overlay travels as
/// <c>--{key}={value}</c> pairs so a <c>Host.CreateApplicationBuilder(args)</c> /
/// <c>Host.CreateDefaultBuilder(args)</c> entry point sees final-precedence values while its
/// <c>Main</c> runs, and <c>--contentRoot</c>/<c>--applicationName</c> keep the worker's own files and
/// identity. An entry point that ignores its arguments still receives the same values at
/// <c>HostBuilding</c> - that in-memory overlay is the fallback, not the primary path.
/// </summary>
internal static class ProtoWorkerArguments
{
    /// <summary>
    /// Builds the entry point's arguments: the worker's content root and application name first, then
    /// one command-line pair per overlay key. A key with a null value has no argument.
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
            if (value is not null)
            {
                arguments.Add($"--{key}={value}");
            }
        }

        return [.. arguments];
    }
}
