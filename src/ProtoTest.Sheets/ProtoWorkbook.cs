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
    /// <summary>The day offset between the 1900 and 1904 date systems.</summary>
    private const int Date1904Offset = 1462;

    /// <summary>The largest area a single merged cell may propagate; beyond it the file is corrupt.</summary>
    private const long MaximumMergeCells = 1_000_000;

    private readonly ProtoExecutionContext? _context;

    private ProtoWorkbook(string name, IReadOnlyList<ProtoSheet> sheets, ProtoExecutionContext? context)
    {
        Name = name;
        Sheets = sheets;
        _context = context;
    }

    /// <summary>The file name the workbook was opened from.</summary>
    public string Name { get; }

    /// <summary>
    /// The sheets that were read. Hidden sheets are omitted unless
    /// <see cref="SheetsOptions.IncludeHiddenSheets"/> is set, so the count reflects visible sheets by
    /// default; each sheet still carries its position in the workbook in <see cref="ProtoSheet.Index"/>.
    /// </summary>
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

    /// <summary>
    /// Binds a record with <c>[Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]</c> to a label/value
    /// sheet: labels in the first column, values in the second, declared once with
    /// <c>[Column("Label")]</c>.
    /// </summary>
    public ProtoKeyValueModel<TModel> KeyValueModel<TModel>() where TModel : notnull
        => ProtoKeyValueModel<TModel>.Read(this, _context);

    /// <summary>Reads a workbook without a test context; used by tests of this package.</summary>
    internal static ProtoWorkbook Read(
        DocumentFormat.OpenXml.Packaging.SpreadsheetDocument document,
        string name,
        ProtoExecutionContext? context,
        SheetsOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        using var operation = context?.Trace
            .Operation("sheets.open", $"Sheets · open {name}", ProtoSheets.TraceSource)
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
            var dateFormats = SheetDateFormat.FromStylesheet(workbookPart.WorkbookStylesPart?.Stylesheet);
            var date1904 = workbookPart.Workbook.WorkbookProperties?.Date1904?.Value ?? false;

            var sheets = new List<ProtoSheet>();
            var index = 0;
            foreach (var sheet in workbookPart.Workbook.Sheets?.Elements<Sheet>() ?? [])
            {
                var sheetName = sheet.Name?.Value ?? $"Sheet{index + 1}";
                var hidden = sheet.State is not null && sheet.State.Value != SheetStateValues.Visible;
                if (!hidden || options.IncludeHiddenSheets)
                {
                    var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
                    var cells = ReadCells(part, sharedStrings, dateFormats, date1904);
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
        SheetDateFormat dateFormats,
        bool date1904)
    {
        var cells = new List<CellData>();
        foreach (var cell in part.Worksheet.Descendants<Cell>())
        {
            var reference = cell.CellReference?.Value;
            if (string.IsNullOrWhiteSpace(reference)
                || !SheetReferences.TryParse(reference, out _, out _))
            {
                // A malformed reference is file corruption; skipping it keeps the rest readable.
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
                if (dateFormats.IsDateStyle(cell.StyleIndex?.Value))
                {
                    // The 1904 date system counts from 1904-01-01, 1462 days after the 1900 epoch.
                    date = DateTime.FromOADate(date1904 ? value + Date1904Offset : value);
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
        // makes a group header spanning columns, and the subheaders under it, read as one table. A corrupt
        // file can repeat a reference; the first definition wins instead of throwing.
        var byReference = new Dictionary<string, CellData>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in cells)
        {
            byReference.TryAdd(cell.Reference, cell);
        }

        foreach (var merge in part.Worksheet.Elements<MergeCells>().SelectMany(merges => merges.Elements<MergeCell>()))
        {
            var range = merge.Reference?.Value;
            if (string.IsNullOrWhiteSpace(range))
            {
                continue;
            }

            var parts = range.Split(':', 2);
            if (parts.Length != 2
                || !SheetReferences.TryParse(parts[0], out var startColumn, out var startRow)
                || !SheetReferences.TryParse(parts[1], out var endColumn, out var endRow))
            {
                continue;
            }

            if (endColumn < startColumn || endRow < startRow)
            {
                continue;
            }

            var area = (long)(endColumn - startColumn + 1) * (endRow - startRow + 1);
            if (area > MaximumMergeCells)
            {
                // A merged area this large is file corruption, not layout; expanding it would
                // materialise millions of cells and exhaust memory.
                continue;
            }

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
}
