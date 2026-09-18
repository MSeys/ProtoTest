namespace ProtoTest.Sheets;

using DocumentFormat.OpenXml.Packaging;
using ProtoTest.Core;

/// <summary>Entry point for opening generated workbooks.</summary>
public sealed class ProtoSheets
{
    private readonly ProtoExecutionContext _context;
    private readonly ProtoSheetsOptions _options;

    internal ProtoSheets(ProtoExecutionContext context, ProtoSheetsOptions options)
    {
        _context = context;
        _options = options;
    }

    /// <summary>Opens a workbook from disk. Any producer's <c>.xlsx</c> works - the file is read as OpenXML.</summary>
    public ProtoWorkbook Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var document = SpreadsheetDocument.Open(fullPath, false);
        return ProtoWorkbook.Read(document, Path.GetFileName(fullPath), _context, _options);
    }

    /// <summary>Opens a workbook from memory, for example a captured attachment.</summary>
    public ProtoWorkbook Open(Stream stream, string name = "workbook.xlsx")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var document = SpreadsheetDocument.Open(stream, false);
        return ProtoWorkbook.Read(document, name, _context, _options);
    }
}
