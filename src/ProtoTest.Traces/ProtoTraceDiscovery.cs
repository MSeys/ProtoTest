namespace ProtoTest.Traces;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

/// <summary>One readable run and the file it lives in.</summary>
public sealed record ProtoTraceRun(ProtoTraceArchive Archive, string TraceFile);

/// <summary>One candidate archive that could not be read, with the reason it was skipped.</summary>
public sealed record ProtoTraceSkippedArchive(string TraceFile, string Reason);

/// <summary>The readable runs one folder holds, newest first, and the candidates that had to be skipped.</summary>
public sealed record ProtoTraceFolderRuns(
    string Root,
    IReadOnlyList<ProtoTraceRun> Runs,
    IReadOnlyList<ProtoTraceSkippedArchive> Skipped);

/// <summary>
/// Finds the <c>.prototrace</c> archives a folder holds. One archive per run; the folder's
/// <c>TestResults/</c> is searched first, and when it yields no readable run the folder tree is
/// walked with build and tooling directories pruned. Inside <c>bin</c> only <c>TestResults</c> folders
/// are read, where a suite's trace lands by default. Recency is the run's recorded start time, not
/// a file timestamp, and an unreadable archive is skipped with its reason instead of failing the scan.
/// </summary>
public static class ProtoTraceDiscovery
{
    private const string BuildOutput = "bin";
    private const string ResultsFolder = "TestResults";

    private static readonly HashSet<string> s_prunedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "obj",
        ".git",
        "node_modules"
    };

    /// <summary>Discovers every readable run under one folder, newest first.</summary>
    public static ProtoTraceFolderRuns Discover(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var root = Path.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Trace folder not found: '{root}'.");
        }

        var runs = new List<ProtoTraceRun>();
        var skipped = new List<ProtoTraceSkippedArchive>();
        var testResults = Path.Combine(root, ResultsFolder);
        if (Directory.Exists(testResults))
        {
            Collect(EnumerateTraces(testResults, prune: false), runs, skipped);
            if (runs.Count == 0)
            {
                // A TestResults folder that yields no readable run must not suppress the tree walk:
                // readable runs elsewhere stay discoverable, and the tree pass re-reads TestResults.
                runs.Clear();
                skipped.Clear();
            }
        }

        if (runs.Count == 0)
        {
            Collect(EnumerateTraces(root, prune: true), runs, skipped);
        }

        runs.Sort(static (left, right) =>
        {
            var byTime = Nullable.Compare(right.Archive.RunStartedAtUtc, left.Archive.RunStartedAtUtc);
            return byTime != 0
                ? byTime
                : string.Compare(left.TraceFile, right.TraceFile, StringComparison.Ordinal);
        });
        skipped.Sort(static (left, right) => string.Compare(left.TraceFile, right.TraceFile, StringComparison.Ordinal));
        return new ProtoTraceFolderRuns(root, runs, skipped);
    }

    /// <summary>
    /// Opens one candidate, or returns why it is not a readable run. The reader's failure list is the
    /// one place an unreadable archive becomes a named reason rather than an exception.
    /// </summary>
    public static bool TryOpen(
        string traceFile,
        [NotNullWhen(true)] out ProtoTraceRun? run,
        [NotNullWhen(false)] out string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(traceFile);
        run = null;
        if (!File.Exists(traceFile))
        {
            reason = "the file does not exist";
            return false;
        }

        try
        {
            run = new ProtoTraceRun(ProtoTraceArchive.Open(traceFile), Path.GetFullPath(traceFile));
            reason = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            reason = exception.Message;
            return false;
        }
    }

    private static void Collect(
        IEnumerable<string> candidates,
        List<ProtoTraceRun> runs,
        List<ProtoTraceSkippedArchive> skipped)
    {
        foreach (var candidate in candidates.OrderBy(file => file, StringComparer.Ordinal))
        {
            if (TryOpen(candidate, out var run, out var reason))
            {
                runs.Add(run);
            }
            else
            {
                skipped.Add(new ProtoTraceSkippedArchive(candidate, reason));
            }
        }
    }

    private static IEnumerable<string> EnumerateTraces(string start, bool prune)
    {
        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] directories;
            try
            {
                files = Directory.GetFiles(directory, "*.prototrace");
                directories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files.OrderBy(file => file, StringComparer.Ordinal))
            {
                yield return file;
            }

            foreach (var subdirectory in directories)
            {
                var name = Path.GetFileName(subdirectory);
                if (prune && string.Equals(name, BuildOutput, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var results in ResultsFoldersIn(subdirectory))
                    {
                        pending.Push(results);
                    }

                    continue;
                }

                if (prune && s_prunedDirectories.Contains(name))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }

    // A test project's default trace lands in bin/<configuration>/<framework>/TestResults; the rest of
    // bin is build output and copied fixtures, never a run. A results folder inside another is read once.
    private static IEnumerable<string> ResultsFoldersIn(string buildOutput)
    {
        try
        {
            var folders = Directory
                .GetDirectories(buildOutput, ResultsFolder, SearchOption.AllDirectories)
                .OrderBy(folder => folder, StringComparer.Ordinal)
                .ToList();
            return folders
                .Where(folder => !folders.Any(outer => folder.StartsWith(outer + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
