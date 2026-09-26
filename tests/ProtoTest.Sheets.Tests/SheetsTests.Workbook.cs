namespace ProtoTest.Sheets.Tests;

using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Json;

public sealed partial class SheetsTests
{

    [SetUp]
    public void CreateWorkbook()
    {
        _path = Path.Combine(Path.GetTempPath(), $"prototest-sheets-{Guid.NewGuid():N}.xlsx");
        using var document = SpreadsheetDocument.Create(_path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Summary"
        });

        var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStrings.SharedStringTable = new SharedStringTable();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new NumberingFormats(
                new NumberingFormat
                {
                    NumberFormatId = 164,
                    FormatCode = "#,##0.00\" USD\""
                },
                new NumberingFormat
                {
                    NumberFormatId = 165,
                    FormatCode = "[$USD] #,##0.00"
                },
                new NumberingFormat
                {
                    NumberFormatId = 166,
                    FormatCode = "[$-409]d-mmm-yy"
                },
                new NumberingFormat
                {
                    NumberFormatId = 167,
                    FormatCode = "[h]:mm:ss"
                })
            { Count = 4 },
            new Fonts(new Font()),
            new Fills(new Fill()),
            new Borders(new Border()),
            new CellFormats(
                new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 165, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 166, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 27, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 167, ApplyNumberFormat = true }));

        Cell Text(string reference, string value)
        {
            sharedStrings.SharedStringTable.AppendChild(new SharedStringItem(new DocumentFormat.OpenXml.Spreadsheet.Text(value)));
            return new Cell
            {
                CellReference = reference,
                DataType = CellValues.SharedString,
                CellValue = new CellValue((sharedStrings.SharedStringTable.ChildElements.Count - 1).ToString(CultureInfo.InvariantCulture))
            };
        }

        Cell Number(string reference, double value) => new()
        {
            CellReference = reference,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
        };

        Cell Bool(string reference, bool value) => new()
        {
            CellReference = reference,
            DataType = CellValues.Boolean,
            CellValue = new CellValue(value ? "1" : "0")
        };

        Cell Formula(string reference, string formula, double cached) => new()
        {
            CellReference = reference,
            CellFormula = new CellFormula { Text = formula },
            CellValue = new CellValue(cached.ToString(CultureInfo.InvariantCulture))
        };

        Cell Date(string reference, DateTime value) => new()
        {
            CellReference = reference,
            StyleIndex = 0,
            CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
        };

        Cell Currency(string reference, double value) => new()
        {
            CellReference = reference,
            StyleIndex = 1,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
        };

        Cell BracketCurrency(string reference, double value) => new()
        {
            CellReference = reference,
            StyleIndex = 2,
            CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
        };

        Cell CustomDate(string reference, DateTime value) => new()
        {
            CellReference = reference,
            StyleIndex = 3,
            CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
        };

        sheetData.Append(
            new Row(
                Text("A1", "Total"),
                Number("B1", 42),
                Bool("C1", true),
                Currency("D1", 1234.5),
                BracketCurrency("E1", 987.65),
                CustomDate("F1", ReportDate)),
            new Row(Formula("A2", "SUM(B1:B1)", 42)),
            new Row(Date("A3", ReportDate)),
            new Row(Text("A4", "Region"), Text("B4", "Amount")),
            new Row(Text("A5", "EMEA"), Text("B5", "1200")));
        worksheetPart.Worksheet.Save();

        // Layered headers: a merged group header over subheaders, with the region merged across rows.
        var salesPart = workbookPart.AddNewPart<WorksheetPart>();
        var salesData = new SheetData();
        salesPart.Worksheet = new Worksheet(salesData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(salesPart),
            SheetId = 2,
            Name = "Sales"
        });
        salesData.Append(
            new Row(Text("A1", "Region"), Text("B1", "FY26")),
            new Row(Text("B2", "Amount"), Text("C2", "Count")),
            new Row(Text("A3", "EMEA"), Number("B3", 1200), Number("C3", 12)),
            new Row(Text("A4", "APAC"), Number("B4", 900), Number("C4", 9)));
        salesPart.Worksheet.Append(
            new MergeCells(new MergeCell { Reference = "A1:A2" }, new MergeCell { Reference = "B1:C1" }));
        salesPart.Worksheet.Save();

        // A ledger with a duplicated numeric amount and an empty amount cell inside the used range.
        var ledgerPart = workbookPart.AddNewPart<WorksheetPart>();
        var ledgerData = new SheetData();
        ledgerPart.Worksheet = new Worksheet(ledgerData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(ledgerPart),
            SheetId = 3,
            Name = "Ledger"
        });
        ledgerData.Append(
            new Row(Text("A1", "Amount"), Text("B1", "Note")),
            new Row(Number("A2", 1200), Text("B2", "first")),
            new Row(Number("A3", 1200), Text("B3", "second")),
            new Row(Text("B4", "missing amount")));
        ledgerPart.Worksheet.Save();

        // A numeric header (a year) must still become a header path.
        var yearsPart = workbookPart.AddNewPart<WorksheetPart>();
        var yearsData = new SheetData();
        yearsPart.Worksheet = new Worksheet(yearsData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(yearsPart),
            SheetId = 4,
            Name = "Years"
        });
        yearsData.Append(
            new Row(Number("A1", 2024)),
            new Row(Text("A2", "value")));
        yearsPart.Worksheet.Save();

        // A numeric key column: the typed key cell must still be findable.
        var keysPart = workbookPart.AddNewPart<WorksheetPart>();
        var keysData = new SheetData();
        keysPart.Worksheet = new Worksheet(keysData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(keysPart),
            SheetId = 5,
            Name = "Keys"
        });
        keysData.Append(
            new Row(Text("A1", "Id"), Text("B1", "Name")),
            new Row(Number("A2", 100), Text("B2", "first")),
            new Row(Number("A3", 200), Text("B3", "second")));
        keysPart.Worksheet.Save();

        // Layered headers whose leaves collide: a table row shape cannot name them apart.
        var groupsPart = workbookPart.AddNewPart<WorksheetPart>();
        var groupsData = new SheetData();
        groupsPart.Worksheet = new Worksheet(groupsData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(groupsPart),
            SheetId = 13,
            Name = "Groups"
        });
        groupsData.Append(
            new Row(Text("A1", "FY26"), Text("B1", "FY25")),
            new Row(Text("A2", "Amount"), Text("B2", "Amount")),
            new Row(Number("A3", 1200), Number("B3", 900)));
        groupsPart.Worksheet.Save();

        // Layered headers whose leaves differ only by case: the case-insensitive shape lookup cannot
        // tell them apart either.
        var caseGroupsPart = workbookPart.AddNewPart<WorksheetPart>();
        var caseGroupsData = new SheetData();
        caseGroupsPart.Worksheet = new Worksheet(caseGroupsData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(caseGroupsPart),
            SheetId = 14,
            Name = "CaseGroups"
        });
        caseGroupsData.Append(
            new Row(Text("A1", "FY26"), Text("B1", "FY25")),
            new Row(Text("A2", "Amount"), Text("B2", "amount")),
            new Row(Number("A3", 1200), Number("B3", 900)));
        caseGroupsPart.Worksheet.Save();

        // Dates for Min/Max constraints, and text-vs-number values for uniqueness.
        var datedPart = workbookPart.AddNewPart<WorksheetPart>();
        var datedData = new SheetData();
        datedPart.Worksheet = new Worksheet(datedData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(datedPart),
            SheetId = 6,
            Name = "Dated"
        });
        datedData.Append(
            new Row(Text("A1", "When")),
            new Row(Date("A2", ReportDate)),
            new Row(Date("A3", ReportDate.AddDays(5))));
        datedPart.Worksheet.Save();

        var mixedPart = workbookPart.AddNewPart<WorksheetPart>();
        var mixedData = new SheetData();
        mixedPart.Worksheet = new Worksheet(mixedData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(mixedPart),
            SheetId = 7,
            Name = "Mixed"
        });
        mixedData.Append(
            new Row(Text("A1", "Code")),
            new Row(Text("A2", "1200")),
            new Row(Number("A3", 1200)));
        mixedPart.Worksheet.Save();

        var mixedDuplicatePart = workbookPart.AddNewPart<WorksheetPart>();
        var mixedDuplicateData = new SheetData();
        mixedDuplicatePart.Worksheet = new Worksheet(mixedDuplicateData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(mixedDuplicatePart),
            SheetId = 12,
            Name = "MixedDuplicate"
        });
        mixedDuplicateData.Append(
            new Row(Text("A1", "Code")),
            new Row(Text("A2", "1200")),
            new Row(Text("A3", "1200")));
        mixedDuplicatePart.Worksheet.Save();

        // Elapsed time and a built-in East Asian date format.
        var formatsPart = workbookPart.AddNewPart<WorksheetPart>();
        var formatsData = new SheetData();
        formatsPart.Worksheet = new Worksheet(formatsData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(formatsPart),
            SheetId = 8,
            Name = "Formats"
        });
        formatsData.Append(
            new Row(new Cell
            {
                CellReference = "A1",
                StyleIndex = 4,
                CellValue = new CellValue("45000")
            }, new Cell
            {
                CellReference = "B1",
                StyleIndex = 5,
                CellValue = new CellValue("0.5")
            }));
        formatsPart.Worksheet.Save();

        // A header-only sheet: the table has columns but no data row.
        var headersPart = workbookPart.AddNewPart<WorksheetPart>();
        var headersData = new SheetData();
        headersPart.Worksheet = new Worksheet(headersData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(headersPart),
            SheetId = 9,
            Name = "Headers"
        });
        headersData.Append(new Row(Text("A1", "Only")));
        headersPart.Worksheet.Save();

        // A hidden sheet: it changes the sheet count only when IncludeHiddenSheets is set.
        var hiddenPart = workbookPart.AddNewPart<WorksheetPart>();
        var hiddenData = new SheetData();
        hiddenPart.Worksheet = new Worksheet(hiddenData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(hiddenPart),
            SheetId = 10,
            Name = "Hidden",
            State = SheetStateValues.Hidden
        });
        hiddenData.Append(new Row(Text("A1", "hidden value")));
        hiddenPart.Worksheet.Save();

        // Corrupt file data: a repeated cell reference and an oversized, reversed merge.
        var corruptPart = workbookPart.AddNewPart<WorksheetPart>();
        var corruptData = new SheetData();
        corruptPart.Worksheet = new Worksheet(corruptData);
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(corruptPart),
            SheetId = 11,
            Name = "Corrupt"
        });
        corruptData.Append(new Row(Text("A1", "first"), Text("A1", "second")));
        corruptPart.Worksheet.Append(
            new MergeCells(
                new MergeCell { Reference = "A1:XFD1048576" },
                new MergeCell { Reference = "B2:A1" }));
        corruptPart.Worksheet.Save();
    }
}
