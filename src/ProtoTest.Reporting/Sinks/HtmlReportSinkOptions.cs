namespace ProtoTest.Reporting;

using ProtoTest.Core;

public sealed class HtmlReportSinkOptions : IProtoFileReportOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Html";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.html");
    public string Title { get; set; } = "ProtoTest Report";
}
