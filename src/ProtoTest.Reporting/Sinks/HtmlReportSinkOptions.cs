namespace ProtoTest.Reporting;

public sealed class HtmlReportSinkOptions : IProtoFileReportOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Html";
    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.html");
    public string Title { get; set; } = "ProtoTest Report";
}
