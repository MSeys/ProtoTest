namespace ProtoTest.Sheets;

using ProtoTest.Core;

/// <summary>Spreadsheet choices, layered from <c>ProtoTest:Sheets</c>.</summary>
public sealed class SheetsOptions : IProtoConfigurableOptions
{
    public const string ConfigurationSectionName = "ProtoTest:Sheets";

    string IProtoConfigurableOptions.ConfigurationSectionName => ConfigurationSectionName;

    /// <summary>
    /// Whether hidden sheets are included when a workbook is opened. The default is false, so
    /// <see cref="ProtoWorkbook.Sheets"/> and the counts derived from it cover only visible sheets;
    /// set this to true to read hidden sheets too.
    /// </summary>
    public bool IncludeHiddenSheets { get; set; }
}
