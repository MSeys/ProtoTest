namespace ProtoTest.Demo;

using ProtoTest.Core;

/// <summary>
/// Finds the Northstar console inside the repository, so both hosting modes serve the same built SPA
/// no matter which directory the test run starts in. The repository root is the folder holding
/// <c>ProtoTest.slnx</c>, discovered by walking up from the test assembly's base directory.
/// </summary>
internal static class ConsoleBuild
{
    public static string? RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The absolute <c>samples/ProtoTest.SampleApp/Ui</c> folder, the page-coverage source.</summary>
    public static string? SourceFolder { get; } = RepositoryRoot is { } root
        ? Path.Combine(root, "samples", "ProtoTest.SampleApp", "Ui")
        : null;

    /// <summary>The absolute <c>samples/ProtoTest.SampleApp/Ui/dist</c> folder the app serves.</summary>
    public static string? DistFolder { get; } = SourceFolder is { } source
        ? Path.Combine(source, "dist")
        : null;

    /// <summary>Whether the console was built; without it the app answers with its not-built page.</summary>
    public static bool IsBuilt => DistFolder is { } dist && File.Exists(Path.Combine(dist, "index.html"));

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
            {
                return directory.FullName;
            }
        }

        return null;
    }
}

/// <summary>
/// Publishes the console build path to the in-process application. Started infrastructure settings are
/// forwarded to the ASP.NET Core host by its initializer, so <c>Northstar:Ui:Path</c> reaches the
/// application without process-wide environment variables.
/// </summary>
internal sealed class NorthstarConsoleBuild : IProtoSettingsInfrastructure
{
    public string Id => "application:northstar-console-build";

    public string Kind => "application";

    public string Description => "Northstar console build";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings => ConsoleBuild.DistFolder is { } dist
        ? new Dictionary<string, string> { ["Northstar:Ui:Path"] = dist }
        : new Dictionary<string, string>();

    public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// Skips the console journeys with a clear reason when the built SPA is missing, so a machine without
/// Node still gets a green, honest run instead of a browser looking at the not-built page.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequiresConsoleBuildAttribute : ProtoAttribute, IProtoSkipCondition
{
    public string? GetSkipReason(ProtoHost host)
        => ConsoleBuild.IsBuilt
            ? null
            : $"The Northstar console is not built; run 'npm run build' in '{ConsoleBuild.SourceFolder ?? "samples/ProtoTest.SampleApp/Ui"}'.";
}
