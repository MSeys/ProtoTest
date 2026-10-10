namespace ProtoTest.RunnerContract.Tests;

using System.Xml.Linq;

/// <summary>One test as the runner reported it: its name, <c>Passed</c>, <c>Failed</c> or <c>Skipped</c>, and its message.</summary>
internal sealed record RunnerResult(string Name, string Outcome, string Message);

/// <summary>Reads the structured result a runner wrote: a TRX file, or xUnit v3's own XML.</summary>
internal static class RunnerResults
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static IReadOnlyList<RunnerResult> Read(string path)
    {
        var document = XDocument.Load(path);
        if (document.Root?.Name == Trx + "TestRun")
        {
            return [.. document.Descendants(Trx + "UnitTestResult").Select(result => new RunnerResult(
                (string?)result.Attribute("testName") ?? string.Empty,
                Normalize((string?)result.Attribute("outcome")),
                result.Element(Trx + "Output") is { } output ? Message(output) : string.Empty))];
        }

        return [.. document.Descendants("test").Select(test => new RunnerResult(
            (string?)test.Attribute("name") ?? string.Empty,
            Normalize((string?)test.Attribute("result")),
            ((string?)test.Element("failure")?.Element("message") ?? string.Empty) + ((string?)test.Element("reason") ?? string.Empty)))];
    }

    private static string Message(XElement output)
        => string.Join(
            Environment.NewLine,
            new[] { output.Element(Trx + "ErrorInfo")?.Element(Trx + "Message"), output.Element(Trx + "StdOut"), output.Element(Trx + "DebugTrace") }
                .Where(element => element is not null)
                .Select(element => element!.Value));

    private static string Normalize(string? outcome) => outcome switch
    {
        "Passed" or "Pass" => "Passed",
        "Failed" or "Fail" or "Error" => "Failed",
        "NotExecuted" or "Skipped" or "Skip" or "Inconclusive" => "Skipped",
        _ => outcome ?? string.Empty
    };
}
