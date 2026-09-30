namespace ProtoTest.Mcp;

/// <summary>
/// Where one MCP server reads its evidence: one trace file, or a project folder whose
/// <c>.prototrace</c> archives the tools discover. Built by <see cref="Resolve"/> from the host's
/// arguments and environment.
/// </summary>
public sealed record ProtoTestMcpOptions
{
    /// <summary>The one argument prefix the stdio host accepts for a single archive.</summary>
    public const string TraceArgument = "--trace";

    /// <summary>The one argument prefix the stdio host accepts for a project folder.</summary>
    public const string ProjectArgument = "--project";

    /// <summary>The environment variable the project folder falls back to.</summary>
    public const string ProjectEnvironmentVariable = "PROTOTEST_PROJECT";

    /// <summary>One trace file; when set, the server reads that archive and nothing else.</summary>
    public string? TracePath { get; init; }

    /// <summary>The folder discovery starts from when no trace file is set; the current directory by default.</summary>
    public string? ProjectDirectory { get; init; }

    /// <summary>Reads exactly one archive.</summary>
    public static ProtoTestMcpOptions ForTrace(string tracePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracePath);
        return new ProtoTestMcpOptions { TracePath = Path.GetFullPath(tracePath) };
    }

    /// <summary>Discovers runs under one folder.</summary>
    public static ProtoTestMcpOptions ForProject(string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        return new ProtoTestMcpOptions { ProjectDirectory = Path.GetFullPath(projectDirectory) };
    }

    /// <summary>
    /// Resolves the discovery inputs with the documented precedence: <c>--trace &lt;file&gt;</c>, then
    /// <c>--project &lt;folder&gt;</c>, then <see cref="ProjectEnvironmentVariable"/>, then the current
    /// directory. An unknown argument or a switch without a value is a configuration error naming it.
    /// </summary>
    public static ProtoTestMcpOptions Resolve(
        string[] args,
        string? environmentProjectDirectory,
        string currentDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);

        string? tracePath = null;
        string? projectDirectory = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (TryValue(argument, TraceArgument, args, ref index, out var traceValue))
            {
                tracePath = traceValue;
                continue;
            }

            if (TryValue(argument, ProjectArgument, args, ref index, out var projectValue))
            {
                projectDirectory = projectValue;
                continue;
            }

            throw new ProtoTestMcpConfigurationException(
                $"Unknown argument '{argument}'.");
        }

        if (!string.IsNullOrWhiteSpace(tracePath))
        {
            return ForTrace(Path.GetFullPath(tracePath, currentDirectory));
        }

        var project = !string.IsNullOrWhiteSpace(projectDirectory)
            ? projectDirectory
            : !string.IsNullOrWhiteSpace(environmentProjectDirectory)
                ? environmentProjectDirectory
                : currentDirectory;
        return ForProject(Path.GetFullPath(project, currentDirectory));
    }

    /// <summary>The stdio host's usage line.</summary>
    public static string Usage
        => $"usage: prototest-mcp [{TraceArgument} <file.prototrace>] [{ProjectArgument} <folder>]";

    private static bool TryValue(
        string argument,
        string name,
        string[] args,
        ref int index,
        out string value)
    {
        if (string.Equals(argument, name, StringComparison.Ordinal))
        {
            if (index + 1 >= args.Length)
            {
                throw new ProtoTestMcpConfigurationException($"'{name}' needs a value.");
            }

            value = args[++index];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ProtoTestMcpConfigurationException($"'{name}' needs a value.");
            }

            return true;
        }

        var prefix = name + "=";
        if (argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = argument[prefix.Length..];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ProtoTestMcpConfigurationException($"'{name}' needs a value.");
            }

            return true;
        }

        value = string.Empty;
        return false;
    }
}

/// <summary>A configuration error the stdio host reports before it starts serving.</summary>
public sealed class ProtoTestMcpConfigurationException(string message) : Exception(message);
