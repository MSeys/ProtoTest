namespace ProtoTest.Feedback;

using ProtoTest.Diagnosis;

/// <summary>
/// The github-annotations channel: one workflow command per failing test, with the selected failure's
/// source location, and one per failed run gate. The lines are a pure rendering of the digest, so a
/// test pins them and the runner shows each failure where it happened.
/// </summary>
public static class ProtoFeedbackAnnotations
{
    /// <summary>Renders the digest's annotation lines; a skipped test is not a failure to annotate.</summary>
    public static IReadOnlyList<string> Lines(ProtoDiagnosisDocument digest)
    {
        ArgumentNullException.ThrowIfNull(digest);

        var lines = new List<string>();
        foreach (var test in digest.Failures)
        {
            if (string.Equals(test.Outcome, "skipped", StringComparison.Ordinal))
            {
                continue;
            }

            var failure = test.Failure;
            lines.Add(ProtoWorkflowCommand.Error(Message(test), failure?.SourceFile, failure?.SourceLine));
        }

        foreach (var gate in digest.Gates)
        {
            if (string.Equals(gate.Verdict, "failed", StringComparison.Ordinal))
            {
                lines.Add(ProtoWorkflowCommand.Error(GateMessage(gate)));
            }
        }

        return lines;
    }

    /// <summary>Writes the annotation lines and reports the channel outcome.</summary>
    public static ProtoFeedbackChannelResult Write(ProtoDiagnosisDocument digest, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(writer);

        var lines = Lines(digest);
        if (lines.Count == 0)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.GithubAnnotations,
                ProtoFeedbackStatuses.Skipped,
                "The run has no failures to annotate.");
        }

        foreach (var line in lines)
        {
            writer.WriteLine(line);
        }

        return new ProtoFeedbackChannelResult(
            ProtoFeedbackChannels.GithubAnnotations,
            ProtoFeedbackStatuses.Posted,
            $"{lines.Count} annotation{(lines.Count == 1 ? string.Empty : "s")}.");
    }

    private static string Message(ProtoDiagnosedTest test)
        => Detail(test) is { Length: > 0 } detail ? $"{test.Name}: {detail}" : test.Name;

    private static string? Detail(ProtoDiagnosedTest test)
    {
        if (test.Failure?.ErrorMessage is { Length: > 0 } message)
        {
            return message;
        }

        if (test.Rule == ProtoDiagnosisRule.Finding && test.Findings.Count > 0)
        {
            return test.Findings[0].Message;
        }

        return test.Rule switch
        {
            ProtoDiagnosisRule.Assertion =>
                $"{test.Mismatches.Count} recorded mismatch{(test.Mismatches.Count == 1 ? string.Empty : "es")}",
            ProtoDiagnosisRule.OperationError => "the failing operation recorded no error message",
            ProtoDiagnosisRule.RunnerFailure => "the runner reported a failure",
            ProtoDiagnosisRule.Finding => "a finding explains the outcome",
            _ => test.UnexplainedReason
        };
    }

    private static string GateMessage(ProtoDiagnosisGate gate)
        => gate.Message is { Length: > 0 } message
            ? $"Run gate '{gate.Name}' failed: {message}"
            : $"Run gate '{gate.Name}' failed.";
}
