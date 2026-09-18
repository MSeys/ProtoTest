namespace ProtoTest.Reporting;

using ProtoTest.Core;

public sealed class JsonReportSinkOptions : IProtoFileReportOptions, IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Reporting:Json";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    public string OutputPath { get; set; } = Path.Combine(
        "TestResults", "ProtoTest", $"report-{Environment.ProcessId}.json");
    public bool Indented { get; set; } = true;
}
