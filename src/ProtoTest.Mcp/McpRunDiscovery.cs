namespace ProtoTest.Mcp;

using ModelContextProtocol;
using ProtoTest.Traces;

/// <summary>
/// Resolves the runs the server's options point at over the reader's folder discovery: the single
/// trace file wins, the project folder falls back, and every failure a tool can meet is a named
/// <see cref="McpException"/> instead of a raw exception the SDK would reword.
/// </summary>
internal static class McpRunDiscovery
{
    /// <summary>Discovers the runs the options point at; a missing file or folder is a named error.</summary>
    public static ProtoTraceFolderRuns Discover(ProtoTestMcpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrWhiteSpace(options.TracePath))
        {
            return new ProtoTraceFolderRuns(options.TracePath, [OpenRun(options.TracePath)], []);
        }

        if (string.IsNullOrWhiteSpace(options.ProjectDirectory))
        {
            throw new McpException("The server has no trace file and no project folder to discover runs in.");
        }

        return DiscoverFolder(options.ProjectDirectory);
    }

    /// <summary>Discovers every readable run under one folder, newest first.</summary>
    public static ProtoTraceFolderRuns DiscoverFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        try
        {
            return ProtoTraceDiscovery.Discover(folder);
        }
        catch (DirectoryNotFoundException)
        {
            throw new McpException($"Project folder not found: '{Path.GetFullPath(folder)}'.");
        }
    }

    /// <summary>Finds the run with the given id, or the newest run when no id is given.</summary>
    public static ProtoTraceRun ResolveRun(ProtoTraceFolderRuns discovered, string? runId)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        if (string.IsNullOrWhiteSpace(runId))
        {
            return discovered.Runs.Count > 0
                ? discovered.Runs[0]
                : throw new McpException(
                    $"No .prototrace archive could be read under '{discovered.Root}'. " +
                    $"Skipped {discovered.Skipped.Count} candidate(s); use list_runs to see the reasons.");
        }

        var matches = discovered.Runs.Where(run => string.Equals(run.Archive.RunId, runId, StringComparison.Ordinal)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            > 1 => throw new McpException(
                $"Run '{runId}' matches {matches.Length} trace files under '{discovered.Root}': " +
                string.Join(", ", matches.Select(match => $"'{match.TraceFile}'")) + "."),
            _ => throw new McpException(
                $"No run '{runId}' among the {discovered.Runs.Count} readable trace(s) under '{discovered.Root}'. " +
                "Use list_runs to see the run ids.")
        };
    }

    private static ProtoTraceRun OpenRun(string traceFile)
    {
        if (!ProtoTraceDiscovery.TryOpen(traceFile, out var run, out var reason))
        {
            throw new McpException($"Could not read trace '{traceFile}': {reason}.");
        }

        return run;
    }
}
