namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>Spreadsheet choices, layered from <c>ProtoTest:Sheets</c>.</summary>
public sealed class ProtoSheetsOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Sheets";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>Whether hidden sheets are included when a workbook is opened.</summary>
    public bool IncludeHiddenSheets { get; set; }
}
