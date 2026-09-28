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
/// <c>TestResults/</c> is searched first, and when nothing lives there the folder tree is walked with
/// build and tooling directories pruned. Recency is the run's recorded start time, not a file
/// timestamp, and an unreadable archive is skipped with its reason instead of failing the scan.
/// </summary>
public static class ProtoTraceDiscovery
{
    private static readonly HashSet<string> s_prunedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin",
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
        foreach (var candidate in Candidates(root))
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

    private static List<string> Candidates(string root)
    {
        var candidates = new List<string>();
        var testResults = Path.Combine(root, "TestResults");
        if (Directory.Exists(testResults))
        {
            candidates.AddRange(EnumerateTraces(testResults, prune: false));
        }

        if (candidates.Count == 0)
        {
            candidates.AddRange(EnumerateTraces(root, prune: true));
        }

        candidates.Sort(StringComparer.Ordinal);
        return candidates;
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
                if (prune && s_prunedDirectories.Contains(Path.GetFileName(subdirectory)))
                {
                    continue;
                }

                pending.Push(subdirectory);
            }
        }
    }
}
