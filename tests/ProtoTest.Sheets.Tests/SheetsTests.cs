namespace ProtoTest.Sheets.Tests;

using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
public sealed class SheetsTests
{
    private static readonly DateTime ReportDate = new(2026, 9, 18);
    private static string _path = null!;

    [OneTimeSetUp]
    public void CreateWorkbook()
    {
        _path = Path.Combine(Path.GetTempPath(), $"prototest-sheets-{Guid.NewGuid():N}.xlsx");
        using var document = SpreadsheetDocument.Create(_path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        workbookPart.Workbook.AppendChild(new Sheets()).Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "Summary"
        });

        var sharedStrings = workbookPart.AddNewPart<SharedStringTablePart>();
        sharedStrings.SharedStringTable = new SharedStringTable();
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
        stylesPart.Stylesheet = new Stylesheet(
            new Fonts(new Font()),
            new Fills(new Fill()),
            new Borders(new Border()),
            new CellFormats(new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }));

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

        sheetData.Append(
            new Row(Text("A1", "Total"), Number("B1", 42), Bool("C1", true)),
            new Row(Formula("A2", "SUM(B1:B1)", 42)),
            new Row(Date("A3", ReportDate)),
            new Row(Text("A4", "Region"), Text("B4", "Amount")),
            new Row(Text("A5", "EMEA"), Text("B5", "1200")));
        worksheetPart.Worksheet.Save();
    }

    [OneTimeTearDown]
    public void DeleteWorkbook() => File.Delete(_path);

    [Test]
    public async Task Open_ShouldReadTypedCellsAndFormulas()
    {
        var (host, context) = await StartAsync("sheets read");
        var workbook = context.Sheets().Open(_path);

        var sheet = workbook.Sheet("Summary");
        sheet.Cell("A1").ShouldBe("Total");
        sheet.Cell("B1").ShouldBe(42);
        sheet.Cell("C1").ShouldBe(true);
        sheet.Cell("A3").ShouldBe(ReportDate);
        Assert.Multiple(() =>
        {
            Assert.That(sheet.Cell("A2").Formula, Is.EqualTo("SUM(B1:B1)"));
            Assert.That(sheet.Cell("A2").Number, Is.EqualTo(42));
            Assert.That(sheet.Cell("Z9").IsEmpty, Is.True);
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Sheet_ShouldFailWithTheAvailableNames()
    {
        var (host, context) = await StartAsync("sheets missing");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Sheet("Missing"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("Summary"));
    }

    [Test]
    public async Task Range_ShouldMatchAndCountCoverage()
    {
        var (host, context) = await StartAsync("sheets range");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        sheet.Range("A4:B5").ShouldMatch(
        [
            ["Region", "Amount"],
            ["EMEA", "1200"]
        ]);

        var coverage = context.Services.GetServices<IProtoCollector>()
            .OfType<SheetsCoverageCollector>()
            .Single()
            .GetReportItems()
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(coverage.Any(item => item.Identifier == "Summary!A4:B5"), Is.True);
            Assert.That(coverage.All(item => item.Category == "Sheets"), Is.True);
        });
    }

    private static async Task<(ProtoHost Host, ProtoExecutionContext Context)> StartAsync(string name)
    {
        var builder = new ProtoHostBuilder();
        builder.AddSheets();
        var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Document), Is.True);
        await host.StartAsync();
        var context = await host.StartTestAsync(name, TestMethod());
        return (host, context);
    }

    private static MethodInfo TestMethod()
        => typeof(SheetsTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
