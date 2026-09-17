namespace ProtoTest.Reporting;

public sealed class JsonReportSinkOptions : IProtoFileReportOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Json";
    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.json");
    public bool Indented { get; set; } = true;
}
