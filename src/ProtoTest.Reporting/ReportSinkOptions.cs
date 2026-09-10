namespace ProtoTest.Reporting;

public sealed class JsonReportSinkOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Json";
    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.json");
    public bool Indented { get; set; } = true;
}

public sealed class HtmlReportSinkOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Html";
    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.html");
    public string Title { get; set; } = "ProtoTest Report";
}
