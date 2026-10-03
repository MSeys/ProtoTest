namespace ProtoTest.Feedback;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoTest.Diagnosis;
using ProtoTest.Verification;

// The comment body: the summary card, one headline, what failed, coverage, then the per-test details folded
// away. It uses only what GitHub renders in a comment (tables, alerts, details), so it reads the same in mail.
internal static partial class FeedbackCommentMarkdown
{
    private const int MaxRows = 20;
    private const int MaxCellText = 160;
    private const int MaxCardRows = 3;
    private const string DocsLink = "https://prototest.dev/docs/continuous-integration";

    // The coverage kinds the card service draws; any other category is sent as "other", so a custom name never leaves.
    private static readonly HashSet<string> CardKinds = new(StringComparer.Ordinal)
    {
        "openapi", "openapi-response", "rest", "rest-traffic", "graphql-operation", "graphql-schema",
        "grpc", "web", "sheets", "device-operations", "wiremock"
    };

    private static readonly string[] KeptWords =
        ["GraphQL", "OpenAPI", "OAuth", "WebSocket", "JSON", "HTTPS", "HTTP", "REST", "API", "SQL", "URL", "UI", "ID", "CSV", "XML"];

    public static string Render(ProtoDiagnosisDocument digest, ProtoFeedbackTarget target)
    {
        var failures = digest.Failures.Where(test => !IsSkipped(test)).ToArray();
        var comparison = target.Comparison;
        var broken = Changed(comparison, ProtoTestChanges.Broken);
        var fixedTests = Changed(comparison, ProtoTestChanges.Fixed);
        var coverage = target.Coverage;
        var added = Findings(coverage, ProtoVerificationFindingClasses.AddedUncovered);
        var regressed = Findings(coverage, ProtoVerificationFindingClasses.Regressed);
        var moved = coverage?.CoverageDeltas
            .Where(delta => delta.Regressed > 0 || delta.AddedUncovered > 0 || delta.PercentageDelta != 0)
            .ToArray() ?? [];
        var gates = digest.Gates.Where(gate => string.Equals(gate.Verdict, "failed", StringComparison.Ordinal)).ToArray();

        var writer = new StringWriter();
        writer.WriteLine(ProtoFeedbackComment.Marker);
        WriteCard(writer, digest, target, broken.Length, failures.Length, fixedTests.Length, added.Length, moved);
        WriteHeadline(writer, digest, target, failures, broken, fixedTests, added.Length, regressed.Length, gates.Length);
        WriteSharedCause(writer, failures);
        WriteFailures(writer, failures, comparison, target.SourceBaseUrl);
        WriteFixed(writer, fixedTests);
        WriteCoverage(writer, digest, coverage, moved);
        WriteUncovered(writer, added, regressed, target.CoverageSuggestions ?? []);
        WriteGates(writer, gates);
        WriteDetails(writer, failures, comparison);
        WriteFooter(writer, digest, comparison);
        return writer.ToString();
    }

    // The card is drawn from counts and built-in coverage kinds only, and only against a base branch run:
    // without one, "0 broke" would claim a comparison that did not happen.
    private static void WriteCard(
        StringWriter writer,
        ProtoDiagnosisDocument digest,
        ProtoFeedbackTarget target,
        int broken,
        int failed,
        int fixedCount,
        int added,
        IReadOnlyList<ProtoVerificationCoverageDelta> moved)
    {
        if (target.SummaryCardUrl is not { } card || target.Comparison is null)
        {
            return;
        }

        var total = digest.Outcomes.Sum(pair => pair.Value);
        var passed = digest.Outcomes.TryGetValue("succeeded", out var succeeded) ? succeeded : 0;
        var rows = moved
            .Where(delta => delta.Baseline.Total > 0 && delta.Current.Total > 0)
            .Take(MaxCardRows)
            .Select(delta => string.Create(
                CultureInfo.InvariantCulture,
                $"{CardKind(delta.Category)}:{delta.Baseline.Covered}/{delta.Baseline.Total}:{delta.Current.Covered}/{delta.Current.Total}"))
            .ToArray();
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"broke={broken}&failed={failed}&fixed={fixedCount}&passed={passed}&total={total}&uncovered={added}");
        if (rows.Length > 0)
        {
            // A comma separates candidates in srcset, so GitHub would cut the dark card's URL at the first one.
            query += $"&cov={string.Join("%2C", rows)}";
        }

        var address = card.AbsoluteUri + (string.IsNullOrEmpty(card.Query) ? "?" : "&") + query;
        var alt = string.Create(
            CultureInfo.InvariantCulture,
            $"ProtoTest: {broken} broke · {failed} failed · {fixedCount} fixed · {passed} of {total} passed · {added} added without a test");
        var link = target.TraceLink is { Length: > 0 } trace ? trace : null;
        if (link is not null)
        {
            writer.WriteLine($"<a href=\"{Attribute(link)}\">");
        }

        writer.WriteLine("<picture>");
        writer.WriteLine($"  <source media=\"(prefers-color-scheme: dark)\" srcset=\"{Attribute(address + "&theme=dark")}\">");
        writer.WriteLine($"  <img src=\"{Attribute(address + "&theme=light")}\" alt=\"{Attribute(alt)}\" width=\"100%\">");
        writer.WriteLine("</picture>");
        if (link is not null)
        {
            writer.WriteLine("</a>");
        }
    }

    private static void WriteHeadline(
        StringWriter writer,
        ProtoDiagnosisDocument digest,
        ProtoFeedbackTarget target,
        IReadOnlyList<ProtoDiagnosedTest> failures,
        IReadOnlyList<ProtoTestComparison> broken,
        IReadOnlyList<ProtoTestComparison> fixedTests,
        int added,
        int regressed,
        int gates)
    {
        var total = digest.Outcomes.Sum(pair => pair.Value);
        var parts = new List<string>();
        if (broken.Count > 0)
        {
            parts.Add($"**{Plural(broken.Count, "test")} broke** against the base branch");
        }

        if (fixedTests.Count > 0)
        {
            parts.Add($"**{Plural(fixedTests.Count, "test")} fixed**");
        }

        if (target.Comparison is not null && broken.Count == 0 && fixedTests.Count == 0)
        {
            parts.Add("No test changed outcome against the base branch");
        }

        var passed = digest.Outcomes.TryGetValue("succeeded", out var succeeded) ? succeeded : 0;
        if (failures.Count == 0)
        {
            // A skipped test did not pass; the footer carries the counts.
            parts.Add(passed != total ? "**No test failed**" : total == 1 ? "**1 test passed**" : $"**All {total} tests passed**");
        }
        else if (failures.Count > broken.Count)
        {
            parts.Add($"**{failures.Count} of {Plural(total, "test")} failed**");
        }

        if (added > 0)
        {
            parts.Add(added == 1 ? "**1 addition** has no test" : $"**{added} additions** have no test");
        }

        if (regressed > 0)
        {
            parts.Add($"**{regressed} no longer covered**");
        }

        if (gates > 0)
        {
            parts.Add($"**{Plural(gates, "run gate")} failed**");
        }

        if (target.TraceLink is { Length: > 0 } link)
        {
            parts.Add($"[**Open the full trace ↗**]({link})");
        }
        else if (digest.TraceFile is { Length: > 0 } traceFile)
        {
            parts.Add($"Trace: {Code(traceFile)}");
        }

        writer.WriteLine();
        writer.WriteLine(string.Join(" · ", parts));
    }

    // Two or more failures on the same check of the same call usually have one cause; say it once, up front.
    private static void WriteSharedCause(StringWriter writer, IReadOnlyList<ProtoDiagnosedTest> failures)
    {
        var group = failures
            .Where(test => test.Mismatches.Count > 0)
            .GroupBy(test => (Kind: test.Failure?.Kind, Subject: Normalize(test.Failure?.Subject), Path: test.Mismatches[0].Path))
            .OrderByDescending(candidate => candidate.Count())
            .FirstOrDefault();
        if (group is null || group.Count() < 2)
        {
            return;
        }

        var on = group.Key.Subject is { Length: > 0 } subject ? $" on {Code(subject)}" : string.Empty;
        writer.WriteLine();
        writer.WriteLine("> [!CAUTION]");
        writer.WriteLine(
            $"> **{group.Count()} of the {failures.Count} failures share one cause:** {Code(group.Key.Path)}{on} differs from what the tests expect.");
    }

    private static void WriteFailures(
        StringWriter writer,
        IReadOnlyList<ProtoDiagnosedTest> failures,
        ProtoTraceComparison? comparison,
        Uri? sourceBase)
    {
        if (failures.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("### What failed");
        writer.WriteLine();
        writer.WriteLine("| | Test | What failed |");
        writer.WriteLine("|:-:|---|---|");
        foreach (var test in failures.Take(MaxRows))
        {
            var change = Change(comparison, test);
            var icon = change == ProtoTestChanges.StillFailing ? "🟠" : "🔴";
            var about = new List<string>();
            if (test.ClassName is { Length: > 0 } className)
            {
                about.Add(Code(className.Split('.')[^1]));
            }

            about.Add(Duration(test.DurationMs));
            if (change == ProtoTestChanges.StillFailing)
            {
                about.Add("already failing on the base branch");
            }

            var what = Summary(test);
            if (Location(test.Failure, sourceBase) is { } location)
            {
                what += $"<br><sub>{location}</sub>";
            }

            writer.WriteLine($"| {icon} | {Cell($"**{Text(Title(test))}**<br><sub>{string.Join(" · ", about)}</sub>")} | {Cell(what)} |");
        }

        if (failures.Count > MaxRows)
        {
            writer.WriteLine($"| | {failures.Count - MaxRows} more | The trace has every failure. |");
        }
    }

    private static void WriteFixed(StringWriter writer, IReadOnlyList<ProtoTestComparison> fixedTests)
    {
        if (fixedTests.Count == 0)
        {
            return;
        }

        var names = fixedTests.Take(MaxRows).Select(test => Text(Title(test.Name))).ToList();
        if (fixedTests.Count > MaxRows)
        {
            names.Add($"and {fixedTests.Count - MaxRows} more");
        }

        writer.WriteLine();
        writer.WriteLine($"✅ **Fixed against the base branch:** {string.Join(", ", names)}");
    }

    private static void WriteCoverage(
        StringWriter writer,
        ProtoDiagnosisDocument digest,
        ProtoVerificationVerdict? coverage,
        IReadOnlyList<ProtoVerificationCoverageDelta> moved)
    {
        var totals = digest.Coverage is { } recorded
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{recorded.Covered} / {recorded.Total} · {recorded.Percentage.ToString("0.#", CultureInfo.InvariantCulture)} %")
            : null;
        if (coverage is null || moved.Count == 0)
        {
            if (coverage is not null || totals is not null)
            {
                writer.WriteLine();
                var parts = new[] { coverage is null ? null : "unchanged against the base branch", digest.Coverage is { } all
                    ? string.Create(CultureInfo.InvariantCulture, $"{all.Covered} of {all.Total} covered ({all.Percentage.ToString("0.#", CultureInfo.InvariantCulture)} %)")
                    : null };
                writer.WriteLine($"**Coverage:** {string.Join(" · ", parts.Where(part => part is not null))}");
            }

            return;
        }

        writer.WriteLine();
        writer.WriteLine("### Coverage");
        writer.WriteLine();
        writer.WriteLine("| Surface | Base branch | This pull request | Change |");
        writer.WriteLine("|---|:-:|:-:|:-:|");
        foreach (var delta in moved.Take(MaxRows))
        {
            var points = Math.Abs(delta.PercentageDelta).ToString("0.#", CultureInfo.InvariantCulture);
            var change = delta.PercentageDelta switch
            {
                < 0 => $"🔻 −{points} pts",
                > 0 => $"🔺 +{points} pts",
                _ => "±0 pts"
            };
            writer.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"| {Cell($"{Text(delta.Category)}<br><sub>{Code(delta.TargetName)}</sub>")} | {delta.Baseline.Covered} / {delta.Baseline.Total} | {delta.Current.Covered} / {delta.Current.Total} | {change} |"));
        }

        if (moved.Count > MaxRows)
        {
            writer.WriteLine($"| {moved.Count - MaxRows} more | | | |");
        }

        if (totals is not null)
        {
            writer.WriteLine($"| All recorded coverage | | {totals} | |");
        }
    }

    // Units the change added without a test, with where to cover each, and the ones it stopped covering.
    private static void WriteUncovered(
        StringWriter writer,
        IReadOnlyList<ProtoVerificationFinding> added,
        IReadOnlyList<ProtoVerificationFinding> regressed,
        IReadOnlyList<ProtoCoverageSuggestion> suggestions)
    {
        if (added.Count == 0 && regressed.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("> [!WARNING]");
        var first = true;
        foreach (var (title, findings) in new[] { ("New and not covered", added), ("No longer covered", regressed) })
        {
            if (findings.Count == 0)
            {
                continue;
            }

            if (!first)
            {
                writer.WriteLine(">");
            }

            first = false;
            writer.WriteLine($"> **{title}**");
            foreach (var finding in findings.Take(MaxRows))
            {
                var suggestion = suggestions.FirstOrDefault(candidate =>
                    string.Equals(candidate.Target, finding.TargetName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Category, finding.Category, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.Path, finding.Identifier, StringComparison.Ordinal));
                var hint = suggestion is null ? string.Empty : $" {Text(OneLine(suggestion.Reason))}";
                var unit = new[] { finding.Identifier is { Length: > 0 } identifier ? Code(identifier) : null, finding.TargetName, finding.Category }
                    .Where(part => !string.IsNullOrEmpty(part))
                    .Select((part, index) => index == 0 && finding.Identifier is { Length: > 0 } ? part! : Text(part!));
                writer.WriteLine($"> - {string.Join(" · ", unit)}.{hint}");
            }

            if (findings.Count > MaxRows)
            {
                writer.WriteLine($"> - {findings.Count - MaxRows} more; the job summary lists them all.");
            }
        }
    }

    private static void WriteGates(StringWriter writer, IReadOnlyList<ProtoDiagnosisGate> gates)
    {
        if (gates.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("> [!WARNING]");
        writer.WriteLine("> **Run gates failed**");
        foreach (var gate in gates)
        {
            writer.WriteLine(gate.Message is { Length: > 0 } message
                ? $"> - {Code(gate.Name)}: {Text(OneLine(message))}"
                : $"> - {Code(gate.Name)}");
        }
    }

    // The full record per failing test, folded so the comment stays short; this is the text the old comment led with.
    private static void WriteDetails(StringWriter writer, IReadOnlyList<ProtoDiagnosedTest> failures, ProtoTraceComparison? comparison)
    {
        if (failures.Count == 0)
        {
            return;
        }

        writer.WriteLine();
        writer.WriteLine("<details>");
        writer.WriteLine($"<summary><b>Failure details</b> · {Plural(failures.Count, "test")}</summary>");
        foreach (var test in failures.Take(MaxRows))
        {
            var lines = DetailLines(test, comparison);
            var fence = new string('`', Math.Max(3, LongestRun(string.Join('\n', lines), '`') + 1));
            writer.WriteLine();
            writer.WriteLine($"#### {Text(Title(test))}");
            writer.WriteLine($"{fence}text");
            foreach (var line in lines)
            {
                writer.WriteLine(line);
            }

            writer.WriteLine(fence);
        }

        if (failures.Count > MaxRows)
        {
            writer.WriteLine();
            writer.WriteLine($"{failures.Count - MaxRows} more failing tests are in the trace.");
        }

        writer.WriteLine();
        writer.WriteLine("</details>");
    }

    private static List<string> DetailLines(ProtoDiagnosedTest test, ProtoTraceComparison? comparison)
    {
        var lines = new List<string> { $"{test.Outcome.ToUpperInvariant()}  {test.Name}  ({Duration(test.DurationMs)})" };
        var failure = test.Failure;
        if (failure is not null)
        {
            // The name is the kind for assertion operations; print the identity once when they match.
            var identity = string.Equals(failure.Kind, failure.Name, StringComparison.Ordinal)
                ? failure.Kind
                : $"{failure.Kind}  {failure.Name}";
            lines.Add($"{identity}  · {failure.Status}");
            if (failure.Subject is { Length: > 0 } subject)
            {
                lines.Add(subject);
            }

            if (failure.ErrorMessage is { Length: > 0 } message)
            {
                lines.AddRange(message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'));
            }
        }

        lines.AddRange(test.Mismatches.Select(mismatch =>
            $"{mismatch.Path}  expected {Value(mismatch.Expected)}  actual {Value(mismatch.Actual)}"));
        if (test.MismatchesTruncated)
        {
            lines.Add($"... more recorded mismatches, capped at {ProtoDiagnosis.MaxMismatches}");
        }

        lines.AddRange(test.Findings.Select(finding => $"finding: {finding.Message} ({finding.Status} {finding.Category})"));
        if (test.Rule is null && test.UnexplainedReason is { Length: > 0 } reason)
        {
            lines.Add($"unexplained: {reason}");
        }

        var compared = comparison?.Tests.FirstOrDefault(candidate => string.Equals(candidate.Name, test.Name, StringComparison.Ordinal));
        if ((compared?.Divergence?.Current ?? compared?.Divergence?.Baseline) is { } where)
        {
            lines.Add($"left the base branch at {where.Kind}  {(where.Subject is { Length: > 0 } subject ? subject : where.Name)}");
        }

        if (failure?.SourceFile is { Length: > 0 } file)
        {
            lines.Add(failure.SourceLine is { } number and > 0 ? $"at {file}:{number}" : $"at {file}");
        }

        return lines;
    }

    private static void WriteFooter(StringWriter writer, ProtoDiagnosisDocument digest, ProtoTraceComparison? comparison)
    {
        var total = digest.Outcomes.Sum(pair => pair.Value);
        var counts = new List<string> { Plural(total, "test") };
        counts.AddRange(digest.Outcomes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Value} {pair.Key}"));
        var against = comparison is null
            ? string.Empty
            : $" compared with base branch run <code>{Html(comparison.BaselineRunId)}</code>";
        writer.WriteLine();
        writer.WriteLine(
            $"<sub>ProtoTest · {Html(string.Join(" · ", counts))} · run <code>{Html(digest.RunId)}</code>{against} · <a href=\"{DocsLink}\">what is this?</a></sub>");
    }

    private static string Summary(ProtoDiagnosedTest test)
    {
        if (test.Mismatches.Count > 0)
        {
            var mismatch = test.Mismatches[0];
            return $"{Code(mismatch.Path)} expected {Code(Clip(Value(mismatch.Expected)))}, got {Code(Clip(Value(mismatch.Actual)))}";
        }

        if (test.Failure?.ErrorMessage is { Length: > 0 } message)
        {
            return Text(Clip(message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0]));
        }

        if (test.Failure is { } failure)
        {
            return $"{Code(failure.Kind)} {Text(failure.Status)}";
        }

        return test.UnexplainedReason is { Length: > 0 } reason ? Text(Clip(OneLine(reason))) : "No failing operation was recorded.";
    }

    // A relative source path links under the commit when the target names one; a rooted path is printed as is.
    private static string? Location(ProtoDiagnosisOperation? failure, Uri? sourceBase)
    {
        if (failure?.SourceFile is not { Length: > 0 } file)
        {
            return null;
        }

        var path = file.Replace('\\', '/');
        var line = failure.SourceLine is { } number and > 0 ? number.ToString(CultureInfo.InvariantCulture) : null;
        var label = path[(path.LastIndexOf('/') + 1)..] + (line is null ? string.Empty : $":{line}");
        if (sourceBase is null || Path.IsPathRooted(path) || path.StartsWith("../", StringComparison.Ordinal))
        {
            return Code(label);
        }

        var url = sourceBase.AbsoluteUri.TrimEnd('/') + "/"
            + string.Join('/', path.Split('/').Select(Uri.EscapeDataString))
            + (line is null ? string.Empty : $"#L{line}");
        return $"[{Text(label)}]({url})";
    }

    // The test's readable title, the way the trace viewer names it: a display name is kept, a method name is split into words.
    private static string Title(ProtoDiagnosedTest test)
    {
        if (test.MethodName is not { Length: > 0 } method)
        {
            return Title(test.Name);
        }

        var prefix = test.ClassName is { Length: > 0 } className ? $"{className}.{method}" : method;
        if (test.Name.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Humanize(method + test.Name[prefix.Length..]);
        }

        return test.Name.Contains(' ', StringComparison.Ordinal) ? test.Name : Humanize(method);
    }

    private static string Title(string name)
    {
        if (name.Contains(' ', StringComparison.Ordinal) && !name.Contains('(', StringComparison.Ordinal))
        {
            return name;
        }

        var open = name.IndexOf('(', StringComparison.Ordinal);
        var head = open >= 0 ? name[..open] : name;
        return Humanize(head[(head.LastIndexOf('.') + 1)..] + (open >= 0 ? name[open..] : string.Empty));
    }

    internal static string Humanize(string name)
    {
        var open = name.IndexOf('(', StringComparison.Ordinal);
        var head = open >= 0 ? name[..open] : name;
        var suffix = open >= 0 ? $" {name[open..]}" : string.Empty;
        var text = head;
        for (var index = 0; index < KeptWords.Length; index++)
        {
            text = text.Replace(KeptWords[index], $" \0{index}\0 ", StringComparison.Ordinal);
        }

        text = SplitLowerUpper().Replace(text.Replace('_', ' '), "$1 $2");
        text = SplitAcronym().Replace(text, "$1 $2");
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word =>
            {
                if (word.Length > 2 && word[0] == '\0' && word[^1] == '\0'
                    && int.TryParse(word[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var kept))
                {
                    return KeptWords[kept];
                }

                var canonical = KeptWords.FirstOrDefault(candidate => string.Equals(candidate, word, StringComparison.OrdinalIgnoreCase));
                if (canonical is not null)
                {
                    return canonical;
                }

                return word.Length >= 2 && word.All(character => char.IsUpper(character) || char.IsDigit(character))
                    ? word
                    : word.ToLowerInvariant();
            })
            .ToArray();
        if (words.Length == 0)
        {
            return name;
        }

        words[0] = char.ToUpperInvariant(words[0][0]) + words[0][1..];
        return string.Join(' ', words) + suffix;
    }

    // Identifiers in a call's subject vary per test; replacing them groups the same call made with different data.
    private static string? Normalize(string? subject)
        => subject is null ? null : NumberSegment().Replace(Guid().Replace(subject, "{id}"), "{id}");

    private static string? Change(ProtoTraceComparison? comparison, ProtoDiagnosedTest test)
        => comparison?.Tests.FirstOrDefault(candidate => string.Equals(candidate.Name, test.Name, StringComparison.Ordinal))?.Change;

    private static ProtoTestComparison[] Changed(ProtoTraceComparison? comparison, string change)
        => comparison?.Tests.Where(test => test.Change == change).ToArray() ?? [];

    private static ProtoVerificationFinding[] Findings(ProtoVerificationVerdict? coverage, string findingClass)
        => coverage?.Findings.Where(finding => finding.Class == findingClass).ToArray() ?? [];

    private static bool IsSkipped(ProtoDiagnosedTest test) => string.Equals(test.Outcome, "skipped", StringComparison.Ordinal);

    private static string CardKind(string category)
    {
        var slug = category.Trim().ToLowerInvariant().Replace(' ', '-');
        return CardKinds.Contains(slug) ? slug : "other";
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Duration(double milliseconds)
        => milliseconds < 1000
            ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds:F0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{milliseconds / 1000:F2} s");

    private static string Value(JsonElement? value)
        => value is null
            ? "null"
            : value.Value.ValueKind == JsonValueKind.String
                ? value.Value.GetString() ?? "null"
                : value.Value.GetRawText();

    private static string OneLine(string text) => WhiteSpace().Replace(text, " ").Trim();

    private static string Clip(string text)
    {
        var line = OneLine(text);
        return line.Length <= MaxCellText ? line : line[..(MaxCellText - 1)] + "…";
    }

    // A code span that survives backticks in its content.
    private static string Code(string text)
    {
        var value = OneLine(text);
        var ticks = new string('`', LongestRun(value, '`') + 1);
        var pad = value.StartsWith('`') || value.EndsWith('`') ? " " : string.Empty;
        return $"{ticks}{pad}{value}{pad}{ticks}";
    }

    // Plain text from a run: Markdown and HTML punctuation is escaped so a message cannot format or hide itself.
    private static string Text(string text) => MarkdownPunctuation().Replace(text, @"\$0");

    // A table cell: one line, and a pipe (also inside a code span) does not end the cell.
    private static string Cell(string content) => UnescapedPipe().Replace(content.ReplaceLineEndings(" "), @"\|");

    private static string Attribute(string value) => Html(value);

    // Only the characters that end text or an attribute; the middle dot and other symbols stay readable.
    private static string Html(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static int LongestRun(string text, char character)
    {
        int longest = 0, current = 0;
        foreach (var candidate in text)
        {
            current = candidate == character ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SplitLowerUpper();

    [GeneratedRegex("([A-Z])([A-Z][a-z])")]
    private static partial Regex SplitAcronym();

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Guid();

    [GeneratedRegex(@"(?<=/)\d+(?=/|\?|$)")]
    private static partial Regex NumberSegment();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"[\\`*_\[\]<>#|~]")]
    private static partial Regex MarkdownPunctuation();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex UnescapedPipe();
}
