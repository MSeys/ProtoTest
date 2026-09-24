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
    [Test]
    public async Task Range_ShouldRejectAReversedReference()
    {
        var (host, context) = Start("sheets reversed range");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => sheet.Range("C10:A1"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("reversed"));
            Assert.That(Coverage(context), Is.Empty, "A rejected range is not a read.");
        });
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
    }

    [Test]
    public async Task Range_ShouldRejectARectangleThatExceedsTheCellLimit()
    {
        var (host, context) = Start("sheets huge range");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => sheet.Range("A1:XFD1048576"));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("supported maximum"));
            Assert.That(Coverage(context), Is.Empty);
        });
        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
    }

    [Test]
    public async Task Cell_ShouldNotRecordCoverageForAnInvalidReference()
    {
        var (host, context) = Start("sheets invalid reference");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        Assert.Throws<FormatException>(() => sheet.Cell("NOT-A-CELL"));
        Assert.That(Coverage(context), Is.Empty, "A reference that failed to parse was never read.");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_RowRange_ShouldBeGuardedWhenThereAreNoDataRows()
    {
        var (host, context) = Start("sheets empty row range");
        var table = context.Sheets().Open(_path).Sheet("Headers").Table(1);
        Assert.That(table.RowCount, Is.Zero);

        var method = typeof(ProtoTable).GetMethod(
            "RowRange", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var range = (string)method.Invoke(table, [2])!;

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(range, Is.EqualTo("Headers"),
            "An empty table must record the sheet, not a malformed row range.");
    }

    [Test]
    public async Task Open_ShouldTolerateCorruptMergesAndDuplicateCells()
    {
        var (host, context) = Start("sheets corrupt data");
        var sheet = context.Sheets().Open(_path).Sheet("Corrupt");

        Assert.Multiple(() =>
        {
            Assert.That(sheet.Cell("A1").Text, Is.EqualTo("first"), "The first duplicate reference wins.");
            Assert.That(sheet.RowCount, Is.EqualTo(1), "An oversized merge is not expanded.");
            Assert.That(sheet.ColumnCount, Is.EqualTo(1));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task NumberFormats_ShouldReadElapsedTimeAndBuiltInDateIds()
    {
        var (host, context) = Start("sheets date format ids");
        var sheet = context.Sheets().Open(_path).Sheet("Formats");

        var builtIn = sheet.Cell("A1");
        var elapsed = sheet.Cell("B1");

        Assert.Multiple(() =>
        {
            Assert.That(builtIn.Date, Is.EqualTo(DateTime.FromOADate(45000)),
                "Built-in date id 27 must read as a date.");
            Assert.That(elapsed.Date, Is.Not.Null, "An [h] elapsed-time format is a time value.");
            Assert.That(elapsed.Number, Is.Null);
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Open_ShouldHonorThe1904DateSystem()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-1904-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook(new WorkbookProperties { Date1904 = true });
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                worksheetPart.Worksheet = new Worksheet(sheetData);
                workbookPart.Workbook.AppendChild(new Sheets()).Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = "Dates"
                });
                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = new Stylesheet(
                    new Fonts(new Font()),
                    new Fills(new Fill()),
                    new Borders(new Border()),
                    new CellFormats(new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));
                sheetData.Append(new Row(new Cell
                {
                    CellReference = "A1",
                    StyleIndex = 0,
                    CellValue = new CellValue("0")
                }));
                worksheetPart.Worksheet.Save();
            }

            var (host, context) = Start("sheets 1904");
            var cell = context.Sheets().Open(path).Sheet("Dates").Cell("A1");

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(cell.Date, Is.EqualTo(new DateTime(1904, 1, 1)),
                "The 1904 date system counts from 1904-01-01, not the 1900 epoch.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task IncludeHiddenSheets_ShouldChangeTheSheetCount()
    {
        var (host, context) = Start("sheets hidden excluded");
        var visible = context.Sheets().Open(_path);
        Assert.That(visible.Sheets.Any(sheet => sheet.Name == "Hidden"), Is.False,
            "Hidden sheets are excluded by default.");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var (includedHost, includedContext) = Start(
            "sheets hidden included",
            options => options.IncludeHiddenSheets = true);
        var included = includedContext.Sheets().Open(_path);

        Assert.Multiple(() =>
        {
            Assert.That(included.Sheets.Any(sheet => sheet.Name == "Hidden"), Is.True,
                "IncludeHiddenSheets adds the hidden sheet to the count.");
            Assert.That(included.Sheet("Hidden").IsHidden, Is.True);
        });
        await includedHost.CompleteTestAsync(ProtoTestResult.Passed);
    }
}
