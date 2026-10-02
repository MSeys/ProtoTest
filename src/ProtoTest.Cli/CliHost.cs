namespace ProtoTest.Cli;

using System.Text.Json;
using ProtoTest.Diagnosis;
using ProtoTest.Feedback;
using ProtoTest.Traces;
using ProtoTest.Verification;

/// <summary>
/// The command-line surface, separated from the entry point so tests can drive it with their own
/// writers. Environment variables carry the feedback channel targets, the GitHub Actions convention.
/// </summary>
public static class CliHost
{
    /// <summary>Runs one command. Returns the process exit code.</summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 2 && string.Equals(args[0], "summary", StringComparison.Ordinal))
        {
            return Summary(args[1], output, error);
        }

        if (args.Length == 2 && string.Equals(args[0], "index", StringComparison.Ordinal))
        {
            return Index(args[1], output, error);
        }

        if (args.Length >= 2 && string.Equals(args[0], "feedback", StringComparison.Ordinal))
        {
            return Feedback(args, output, error);
        }

        if (args.Length is 3 or 4 && string.Equals(args[0], "verify", StringComparison.Ordinal))
        {
            if (args.Length == 4 && !string.Equals(args[3], "--strict", StringComparison.Ordinal))
            {
                WriteUsage(error);
                return 1;
            }

            return Verify(args[1], args[2], strict: args.Length == 4, output, error);
        }

        if (args.Length == 3 && string.Equals(args[0], "compare", StringComparison.Ordinal))
        {
            return Compare(args[1], args[2], output, error);
        }

        if (args.Length >= 3 && string.Equals(args[0], "prove", StringComparison.Ordinal))
        {
            return Prove(args, output, error);
        }

        if (args.Length >= 2 && string.Equals(args[0], "review", StringComparison.Ordinal))
        {
            return Review(args, output, error);
        }

        WriteUsage(error);
        return 1;
    }

    private static int Summary(string path, TextWriter output, TextWriter error)
    {
        if (!File.Exists(path))
        {
            error.WriteLine($"Trace file not found: {path}");
            return 1;
        }

        try
        {
            ProtoTraceSummaryText.Write(ProtoDiagnosis.Read(path), output);
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not read '{path}': {exception.Message}");
            return 1;
        }
    }

    private static int Index(string folder, TextWriter output, TextWriter error)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            WriteUsage(error);
            return 1;
        }

        ProtoTraceFolderRuns discovered;
        try
        {
            discovered = ProtoTraceDiscovery.Discover(folder);
        }
        catch (DirectoryNotFoundException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"Could not read '{folder}': {exception.Message}");
            return 1;
        }

        if (discovered.Runs.Count == 0)
        {
            error.WriteLine($"No readable .prototrace archive under '{discovered.Root}' ({discovered.Skipped.Count} skipped).");
            foreach (var skip in discovered.Skipped)
            {
                error.WriteLine($"  {skip.TraceFile}: {skip.Reason}");
            }

            return 1;
        }

        try
        {
            var result = ProtoTraceIndex.Write(discovered);
            output.WriteLine($"Indexed {Runs(result.Runs)} into '{result.PagePath}'.");
            foreach (var skip in result.Skipped)
            {
                output.WriteLine($"Skipped '{skip.TraceFile}': {skip.Reason}");
            }

            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error.WriteLine($"Could not write the index under '{discovered.Root}': {exception.Message}");
            return 1;
        }
    }

    private static int Feedback(string[] args, TextWriter output, TextWriter error)
    {
        string? digestPath = null;
        string? baselinePath = null;
        for (var index = 2; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--digest", StringComparison.Ordinal) && index + 1 < args.Length)
            {
                digestPath = args[++index];
                continue;
            }

            if (string.Equals(args[index], "--baseline", StringComparison.Ordinal) && index + 1 < args.Length)
            {
                baselinePath = args[++index];
                continue;
            }

            WriteUsage(error);
            return 1;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            error.WriteLine($"Trace file not found: {path}");
            return 1;
        }

        ProtoDiagnosisDocument digest;
        try
        {
            digest = ProtoFeedback.ReadDigest(path);
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not read '{path}': {exception.Message}");
            return 1;
        }

        if (digestPath is { Length: > 0 })
        {
            try
            {
                File.WriteAllText(digestPath, ProtoFeedback.DigestJson(digest));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error.WriteLine($"Could not write '{digestPath}': {exception.Message}");
                return 1;
            }
        }

        ProtoTraceComparison? comparison = null;
        ProtoVerificationVerdict? coverage = null;
        IReadOnlyList<ProtoCoverageSuggestion> suggestions = [];
        if (baselinePath is { Length: > 0 })
        {
            if (!File.Exists(baselinePath))
            {
                error.WriteLine($"Baseline trace not found: {baselinePath}");
                return 1;
            }

            try
            {
                comparison = ProtoVerification.Compare(baselinePath, path);
            }
            catch (Exception exception)
            {
                error.WriteLine($"Could not compare with '{baselinePath}': {exception.Message}");
                return 1;
            }

            (coverage, suggestions) = CoverageAgainst(baselinePath, path, error);
        }

        ProtoFeedbackReport report;
        using var client = new HttpClient();
        try
        {
            var target = Target() with { Comparison = comparison, Coverage = coverage, CoverageSuggestions = suggestions };
            report = ProtoFeedback.PostAsync(digest, target, client, output).GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            error.WriteLine($"Feedback failed: {exception.Message}");
            return 1;
        }

        foreach (var channel in report.Channels)
        {
            var reason = channel.Reason is { Length: > 0 } text ? $" ({text})" : string.Empty;
            error.WriteLine($"prototest feedback: {channel.Channel} {channel.Status}{reason}");
        }

        return report.Failed ? 1 : 0;
    }

    // The coverage verdict needs both runs to embed a report; without one the comment leaves coverage out.
    private static (ProtoVerificationVerdict? Verdict, IReadOnlyList<ProtoCoverageSuggestion> Suggestions) CoverageAgainst(
        string baselinePath,
        string currentPath,
        TextWriter error)
    {
        try
        {
            var verdict = ProtoVerification.Verify(ProtoVerificationRun.FromTrace(baselinePath), ProtoVerificationRun.FromTrace(currentPath));
            return (verdict, ProtoDiagnosis.SuggestCoverage(ProtoTraceArchive.Open(currentPath)));
        }
        catch (InvalidOperationException exception)
        {
            error.WriteLine($"prototest feedback: coverage left out ({exception.Message})");
            return (null, []);
        }
    }

    private static int Verify(string baselinePath, string currentPath, bool strict, TextWriter output, TextWriter error)
    {
        foreach (var path in new[] { baselinePath, currentPath })
        {
            if (!File.Exists(path))
            {
                error.WriteLine($"Report or trace file not found: {path}");
                return 1;
            }
        }

        ProtoVerificationVerdict verdict;
        try
        {
            // --strict fails a unit the change added without a test, not only one it stopped covering.
            var options = strict
                ? ProtoVerificationOptions.Default with { AddedUncoveredSeverity = ProtoVerificationSeverities.Fail }
                : ProtoVerificationOptions.Default;
            verdict = ProtoVerification.Verify(Run(baselinePath), Run(currentPath), options: options);
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not verify: {exception.Message}");
            return 1;
        }

        foreach (var finding in verdict.Findings.Where(finding => finding.Severity == ProtoVerificationSeverities.Fail))
        {
            output.WriteLine(ProtoWorkflowCommand.Error($"{finding.Class}: {finding.Message}"));
        }

        ProtoVerificationText.Write(verdict, output);
        return verdict.Failed ? 1 : 0;
    }

    // A trace carries the report its run embedded, so verify reads either file.
    private static ProtoVerificationRun Run(string path)
        => path.EndsWith(".prototrace", StringComparison.OrdinalIgnoreCase)
            ? ProtoVerificationRun.FromTrace(path)
            : ProtoVerificationRun.FromReportFile(path);

    private static int Compare(string baselinePath, string currentPath, TextWriter output, TextWriter error)
    {
        foreach (var path in new[] { baselinePath, currentPath })
        {
            if (!File.Exists(path))
            {
                error.WriteLine($"Trace file not found: {path}");
                return 1;
            }
        }

        ProtoTraceComparison comparison;
        try
        {
            comparison = ProtoVerification.Compare(baselinePath, currentPath);
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not compare: {exception.Message}");
            return 1;
        }

        foreach (var test in comparison.Tests.Where(test => test.Change == ProtoTestChanges.Broken))
        {
            output.WriteLine(ProtoWorkflowCommand.Error($"broken: {test.Name}"));
        }

        ProtoVerificationText.Write(comparison, output);
        return comparison.HasBroken ? 1 : 0;
    }

    private static int Prove(string[] args, TextWriter output, TextWriter error)
    {
        var traces = new List<string>();
        var tests = new List<string>();
        for (var index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--test", StringComparison.Ordinal))
            {
                if (index + 1 >= args.Length)
                {
                    WriteUsage(error);
                    return 1;
                }

                tests.Add(args[++index]);
                continue;
            }

            traces.Add(args[index]);
        }

        if (traces.Count < 2)
        {
            WriteUsage(error);
            return 1;
        }

        foreach (var path in traces)
        {
            if (!File.Exists(path))
            {
                error.WriteLine($"Trace file not found: {path}");
                return 1;
            }
        }

        ProtoFixReceipt receipt;
        try
        {
            receipt = ProtoVerification.Prove(traces[0], traces.Skip(1).ToArray(), tests);
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not prove: {exception.Message}");
            return 1;
        }

        foreach (var test in receipt.Tests.Where(test => !test.Proven))
        {
            output.WriteLine(ProtoWorkflowCommand.Error($"not proven: {test.Name}"));
        }

        foreach (var name in receipt.BrokenTests)
        {
            output.WriteLine(ProtoWorkflowCommand.Error($"broken: {name}"));
        }

        ProtoVerificationText.Write(receipt, output);
        return receipt.Proven ? 0 : 1;
    }

    private static int Review(string[] args, TextWriter output, TextWriter error)
    {
        var tests = new List<string>();
        for (var index = 2; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--test", StringComparison.Ordinal) && index + 1 < args.Length)
            {
                tests.Add(args[++index]);
                continue;
            }

            WriteUsage(error);
            return 1;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            error.WriteLine($"Trace file not found: {path}");
            return 1;
        }

        try
        {
            ProtoReviewText.Write(ProtoDiagnosis.Review(path, tests), output);
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine($"Could not review '{path}': {exception.Message}");
            return 1;
        }
    }

    private static ProtoFeedbackTarget Target()
        => new()
        {
            Token = Value("GITHUB_TOKEN"),
            Repository = Value("GITHUB_REPOSITORY"),
            PullRequestNumber = PullRequestNumber(),
            FromFork = FromFork(),
            ApiUrl = AbsoluteUrl("GITHUB_API_URL"),
            TraceLink = Value("PROTOTEST_FEEDBACK_TRACE_URL"),
            WebhookUrl = AbsoluteUrl("PROTOTEST_FEEDBACK_WEBHOOK_URL"),
            WebhookSecret = Value("PROTOTEST_FEEDBACK_WEBHOOK_SECRET"),
            WebhookSecretHeader = Value("PROTOTEST_FEEDBACK_WEBHOOK_SECRET_HEADER")
        };

    private static string? Value(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Uri? AbsoluteUrl(string name)
    {
        var value = Value(name);
        if (value is null)
        {
            return null;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException($"'{value}' is not an absolute URL for {name}.");
    }

    private static int? PullRequestNumber()
    {
        var path = Value("GITHUB_EVENT_PATH");
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return SectionNumber(root, "pull_request")
                ?? SectionNumber(root, "issue")
                ?? (root.TryGetProperty("number", out var number) && number.TryGetInt32(out var value) ? value : null);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    // A pull request from a fork names a head repository other than its base repository.
    private static bool FromFork()
    {
        var path = Value("GITHUB_EVENT_PATH");
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("pull_request", out var pullRequest)
                && pullRequest.ValueKind == JsonValueKind.Object
                && RepositoryName(pullRequest, "head") is { } head
                && RepositoryName(pullRequest, "base") is { } baseRepository
                && !string.Equals(head, baseRepository, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return false;
        }
    }

    private static string? RepositoryName(JsonElement pullRequest, string side)
        => pullRequest.TryGetProperty(side, out var reference)
            && reference.ValueKind == JsonValueKind.Object
            && reference.TryGetProperty("repo", out var repository)
            && repository.ValueKind == JsonValueKind.Object
            && repository.TryGetProperty("full_name", out var name)
            && name.ValueKind == JsonValueKind.String
                ? name.GetString()
                : null;

    private static int? SectionNumber(JsonElement root, string name)
        => root.TryGetProperty(name, out var section)
            && section.ValueKind == JsonValueKind.Object
            && section.TryGetProperty("number", out var number)
            && number.TryGetInt32(out var value)
                ? value
                : null;

    private static void WriteUsage(TextWriter writer)
        => writer.WriteLine(
            """
            usage: prototest summary <file.prototrace>
                   prototest index <folder>
                   prototest feedback <file.prototrace> [--digest <path>] [--baseline <file.prototrace>]
                   prototest verify <baseline> <current> [--strict]   (report.json or .prototrace)
                   prototest compare <baseline.prototrace> <current.prototrace>
                   prototest prove <baseline.prototrace> <current.prototrace>... [--test <name>]...
                   prototest review <file.prototrace> [--test <name>]...
            """);

    private static string Runs(int count)
        => count == 1 ? "1 run" : $"{count} runs";
}
