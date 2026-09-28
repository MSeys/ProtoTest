namespace ProtoTest.Cli;

using System.Text;
using System.Text.Json;
using ProtoTest.Diagnosis;
using ProtoTest.Traces;

/// <summary>
/// Writes the static index over a folder of runs: one page linking every run's archive and digest,
/// and one digest file per run beside its archive. No server, no script and no second reader; the
/// digest is the one <see cref="ProtoDiagnosisJson"/> document, written from the reader's archive.
/// </summary>
internal static class ProtoTraceIndex
{
    /// <summary>The page's file name in the indexed folder.</summary>
    public const string PageFileName = "index.html";

    /// <summary>The digest file's suffix beside a run's archive.</summary>
    public const string DigestSuffix = ".digest.json";

    /// <summary>
    /// Reads every discovered run's diagnosis once, writes each digest beside its archive and the page
    /// into the folder. A run whose archive opened but whose evidence cannot be read is skipped with
    /// its reason, the same way an unreadable candidate is.
    /// </summary>
    public static ProtoTraceIndexResult Write(ProtoTraceFolderRuns discovered)
    {
        ArgumentNullException.ThrowIfNull(discovered);
        var runs = new List<IndexedRun>();
        var skipped = new List<ProtoTraceSkippedArchive>(discovered.Skipped);
        foreach (var run in discovered.Runs)
        {
            ProtoDiagnosisDocument diagnosis;
            try
            {
                diagnosis = ProtoDiagnosis.Read(run.Archive);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or JsonException)
            {
                skipped.Add(new ProtoTraceSkippedArchive(run.TraceFile, exception.Message));
                continue;
            }

            var digestPath = Path.ChangeExtension(run.TraceFile, null) + DigestSuffix;
            File.WriteAllText(
                digestPath,
                ProtoDiagnosisJson.ToJson(diagnosis),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            runs.Add(new IndexedRun(run, diagnosis, digestPath));
        }

        skipped.Sort(static (left, right) => string.Compare(left.TraceFile, right.TraceFile, StringComparison.Ordinal));
        var pagePath = Path.Combine(discovered.Root, PageFileName);
        File.WriteAllText(
            pagePath,
            ProtoTraceIndexHtml.Render(discovered.Root, runs, skipped),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return new ProtoTraceIndexResult(pagePath, runs.Count, skipped);
    }
}

/// <summary>One run the page carries: the discovered archive, its diagnosis and the digest written for it.</summary>
internal sealed record IndexedRun(ProtoTraceRun Run, ProtoDiagnosisDocument Diagnosis, string DigestPath);

/// <summary>What one index write produced: where the page is, how many runs it carries and what was skipped.</summary>
internal sealed record ProtoTraceIndexResult(string PagePath, int Runs, IReadOnlyList<ProtoTraceSkippedArchive> Skipped);
