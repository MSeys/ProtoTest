namespace ProtoTest.Sheets;

using DocumentFormat.OpenXml.Spreadsheet;
using ProtoTest.Core;
using ProtoTest.Sheets.Internal;

/// <summary>
/// An opened workbook. Reading is eager and complete: tests assert on values, so a missing sheet or
/// reference must fail with a clear message rather than a lazy parser error later.
/// </summary>
public sealed class ProtoWorkbook
{
    private readonly ProtoExecutionContext? _context;

    private ProtoWorkbook(string name, IReadOnlyList<ProtoSheet> sheets, ProtoExecutionContext? context)
    {
        Name = name;
        Sheets = sheets;
        _context = context;
    }

    /// <summary>The file name the workbook was opened from.</summary>
    public string Name { get; }

    public IReadOnlyList<ProtoSheet> Sheets { get; }

    /// <summary>Finds a sheet by name; the failure lists the sheets that do exist.</summary>
    public ProtoSheet Sheet(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Sheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal))
            ?? throw new SpreadsheetAssertionException(
                $"The workbook '{Name}' has no sheet '{name}'. It has: {string.Join(", ", Sheets.Select(sheet => sheet.Name))}.");
    }

    /// <summary>
    /// Binds a record with <c>[Sheet]</c>/<c>[Column]</c> attributes to this workbook. The record is the
    /// model: the layout is declared once and the tests read typed rows.
    /// </summary>
    public ProtoSheetModel<TRow> Model<TRow>() where TRow : notnull
        => ProtoSheetModel<TRow>.Read(this, _context);

    /// <summary>Reads a workbook without a test context; used by tests of this package.</summary>
    internal static ProtoWorkbook Read(
        DocumentFormat.OpenXml.Packaging.SpreadsheetDocument document,
        string name,
        ProtoExecutionContext? context,
        ProtoSheetsOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        using var operation = context?.Trace
            .Operation("sheets.open", $"Sheets · open {name}", "ProtoTest.Sheets")
            .With("sheets.name", name)
            .Begin();
        try
        {
            var workbookPart = document.WorkbookPart
                ?? throw new SpreadsheetAssertionException($"'{name}' is not a spreadsheet workbook.");
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable
                .Elements<SharedStringItem>()
                .Select(item => item.InnerText)
                .ToArray() ?? [];
            var isDateStyle = DateStyles(workbookPart.WorkbookStylesPart?.Stylesheet);

            var sheets = new List<ProtoSheet>();
            var index = 0;
            foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
            {
                var sheetName = sheet.Name?.Value ?? $"Sheet{index + 1}";
                var hidden = sheet.State is not null && sheet.State.Value != SheetStateValues.Visible;
                if (!hidden || options.IncludeHiddenSheets)
                {
                    var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
                    var cells = ReadCells(part, sharedStrings, isDateStyle);
                    sheets.Add(new ProtoSheet(
                        sheetName,
                        index,
                        hidden,
                        cells,
                        cells.Count == 0 ? 0 : cells.Max(cell => SheetReferences.Parse(cell.Reference).Row),
                        cells.Count == 0 ? 0 : cells.Max(cell => SheetReferences.Parse(cell.Reference).Column),
                        context));
                }

                index++;
            }

            var workbook = new ProtoWorkbook(name, sheets, context);
            operation?.AddSection(new ProtoTraceSection(
                "Sheets",
                ProtoTraceSectionKind.Fields,
                Items: [.. sheets.Select(sheet => new ProtoTraceSectionItem(
                    sheet.Name,
                    $"{sheet.RowCount}x{sheet.ColumnCount}{(sheet.IsHidden ? " hidden" : string.Empty)}"))]));
            operation?.Succeed();
            context?.RecordObservation(new ProtoObservation(
                "Sheets",
                "sheets.workbook",
                name,
                Metadata: new Dictionary<string, object> { ["sheets.count"] = sheets.Count }));
            return workbook;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }

    private static List<CellData> ReadCells(
        DocumentFormat.OpenXml.Packaging.WorksheetPart part,
        IReadOnlyList<string> sharedStrings,
        Func<uint?, bool> isDateStyle)
    {
        var cells = new List<CellData>();
        foreach (var cell in part.Worksheet.Descendants<Cell>())
        {            var reference = cell.CellReference?.Value;
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            string? text = null;
            double? number = null;
            bool? boolean = null;
            DateTime? date = null;
            var raw = cell.CellValue?.InnerText;
            var type = cell.DataType?.Value;
            if (type == CellValues.SharedString
                && int.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out var sharedIndex)
                && sharedIndex >= 0
                && sharedIndex < sharedStrings.Count)
            {
                text = sharedStrings[sharedIndex];
            }
            else if (type == CellValues.InlineString)
            {
                text = cell.InlineString?.InnerText;
            }
            else if (type == CellValues.Boolean)
            {
                boolean = raw == "1";
            }
            else if (type == CellValues.String || type == CellValues.Error)
            {
                text = raw;
            }
            else if (raw is not null
                && double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                if (isDateStyle(cell.StyleIndex?.Value))
                {
                    date = DateTime.FromOADate(value);
                }
                else
                {
                    number = value;
                }
            }
            else if (raw is not null)
            {
                text = raw;
            }

            cells.Add(new CellData(reference.ToUpperInvariant(), text, number, boolean, date, cell.CellFormula?.Text));
        }

        // Merged cells store their value in the top-left cell only; propagating it right and down is what
        // makes a group header spanning columns, and the subheaders under it, read as one table.
        var byReference = cells.ToDictionary(cell => cell.Reference, StringComparer.OrdinalIgnoreCase);
        foreach (var merge in part.Worksheet.Elements<MergeCells>().SelectMany(merges => merges.Elements<MergeCell>()))
        {
            var range = merge.Reference?.Value;
            if (string.IsNullOrWhiteSpace(range))
            {
                continue;
            }

            var parts = range.Split(':', 2);
            if (parts.Length != 2)
            {
                continue;
            }

            var (startColumn, startRow) = SheetReferences.Parse(parts[0]);
            var (endColumn, endRow) = SheetReferences.Parse(parts[1]);
            if (!byReference.TryGetValue(SheetReferences.Format(startColumn, startRow), out var source))
            {
                continue;
            }

            for (var row = startRow; row <= endRow; row++)
            {
                for (var column = startColumn; column <= endColumn; column++)
                {
                    var reference = SheetReferences.Format(column, row);
                    if (!byReference.ContainsKey(reference))
                    {
                        byReference[reference] = source with { Reference = reference };
                    }
                }
            }
        }

        return [.. byReference.Values];
    }

    /// <summary>Dates are numeric cells with a date number format; built-in ids plus a format-code check.</summary>
    private static Func<uint?, bool> DateStyles(Stylesheet? stylesheet)
    {
        if (stylesheet?.CellFormats is null)
        {
            return _ => false;
        }

        var formats = stylesheet.CellFormats.Elements<CellFormat>().ToArray();
        var customFormats = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
            .Where(format => format.NumberFormatId is not null && format.FormatCode is not null)
            .ToDictionary(format => format.NumberFormatId!.Value, format => format.FormatCode!.Value)
            ?? [];
        return styleIndex =>
        {
            if (styleIndex is null || styleIndex.Value >= formats.Length)
            {
                return false;
            }

            var formatId = formats[(int)styleIndex.Value].NumberFormatId?.Value ?? 0;
            if (formatId is >= 14 and <= 22 or >= 45 and <= 47)
            {
                return true;
            }

            if (!customFormats.TryGetValue(formatId, out var code) || string.IsNullOrEmpty(code))
            {
                return false;
            }

            return code.Contains('y', StringComparison.OrdinalIgnoreCase)
                || code.Contains('d', StringComparison.OrdinalIgnoreCase)
                || code.Contains('h', StringComparison.OrdinalIgnoreCase);
        };
    }
}
