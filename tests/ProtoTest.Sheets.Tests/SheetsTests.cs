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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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

        // Dates for Min/Max constraints, and text-vs-number values for uniqueness.
        var datedPart = workbookPart.AddNewPart<WorksheetPart>();
        var datedData = new SheetData();
        datedPart.Worksheet = new Worksheet(datedData);
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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
        workbookPart.Workbook.Sheets.Append(new Sheet
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

    [OneTimeTearDown]
    public void DeleteWorkbook() => File.Delete(_path);

    [Test]
    public async Task Open_ShouldReadTypedCellsAndFormulas()
    {
        var (host, context) = await StartAsync("sheets read");
        var workbook = context.Sheets().Open(_path);

        var sheet = workbook.Sheet("Summary");
        sheet.Cell("A1").Should.Be("Total");
        sheet.Cell("B1").Should.Be(42);
        sheet.Cell("C1").Should.Be(true);
        sheet.Cell("A3").Should.Be(ReportDate);
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

        sheet.Range("A4:B5").Should.Match(
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

    [Test]
    public async Task Table_ShouldHandleLayeredHeadersAndMergedGroups()
    {
        var (host, context) = await StartAsync("sheets table");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        Assert.Multiple(() =>
        {
            Assert.That(table.Headers[1], Is.EqualTo(new[] { "FY26", "Amount" }));
            Assert.That(table.Headers[2], Is.EqualTo(new[] { "FY26", "Count" }));
        });

        table.RowWhere("Region", "EMEA")["FY26", "Amount"].Should.Be(1200.0);
        table.RowWhere("Region", "APAC")["Amount"].Should.Be(900.0);
        table.Column("Amount").Should.Be(["1200", "900"]);
        table.Column("FY26", "Count").Should.Be(["12", "9"]);
        table.Should.ContainRow("Region", "APAC");
        Assert.Throws<SpreadsheetAssertionException>(() =>
        {
            _ = table.Column("Missing");
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_ShouldNotRecordCoverageUntilRead()
    {
        var (host, context) = await StartAsync("sheets table untouched");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        Assert.That(table.Headers, Has.Count.EqualTo(3));
        Assert.That(Coverage(context), Is.Empty, "Building the view is not reading the data.");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_ShouldRecordOnlyTheRangesActuallyRead()
    {
        var (host, context) = await StartAsync("sheets table coverage");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        _ = table.Column("Amount");
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EqualTo(new[] { "Sales!B3:B4" }), "Reading one column covers only that column.");

        _ = table.RowWhere("Region", "EMEA");
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EquivalentTo(new[] { "Sales!B3:B4", "Sales!A3:C4" }));

        var apac = table.RowWhere("Region", "APAC");
        _ = apac["FY26", "Amount"];
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EquivalentTo(new[] { "Sales!B3:B4", "Sales!A3:C4", "Sales!A4:C4" }));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Sheet("Sales", HeaderRows = [1, 2])]
    public sealed record SalesRow(
        [property: Column("Region", Pattern = "^[A-Z]+$", Unique = true)] string Region,
        [property: Column("FY26", "Amount", Min = 0)] decimal Amount,
        [property: Column("FY26", "Count", Min = 0)] int Count);

    [Sheet("Sales", HeaderRows = [1, 2])]
    public sealed record StrictSalesRow(
        [property: Column("Region")] string Region,
        [property: Column("FY26", "Amount", Min = 1000)] decimal Amount);

    [Sheet("Sales", HeaderRows = [1, 2])]
    public sealed record BrokenRow(
        [property: Column("Region")] string Region,
        [property: Column("Nope")] string Missing);

    [Sheet("Ledger")]
    public sealed record LedgerRow(
        [property: Column("Amount", Unique = true)] decimal Amount,
        [property: Column("Note", Optional = true)] string? Note);

    [Sheet("Ledger")]
    public sealed record OptionalLedgerRow(
        [property: Column("Amount", Optional = true)] decimal? Amount);

    [Sheet("Mixed")]
    public sealed record MixedCodeRow(
        [property: Column("Code", Unique = true)] string Code);

    [Sheet("MixedDuplicate")]
    public sealed record DuplicateCodeRow(
        [property: Column("Code", Unique = true)] string Code);

    [Sheet("Dated")]
    public sealed record MinDateRow(
        [property: Column("When", Min = 50000)] DateTime When);

    [Sheet("Dated")]
    public sealed record MaxDateRow(
        [property: Column("When", Max = 1000)] DateTime When);

    [Sheet("Keys")]
    public sealed record NumericConstraintRow(
        [property: Column("Id", Pattern = "^1\\d{2}$", OneOf = ["100"])] decimal Id,
        [property: Column("Name")] string Name);

    [Sheet("Ledger")]
    public sealed record OptionalValueRow(
        [property: Column("Amount", Optional = true)] decimal Amount);

    [Sheet("Sales", HeaderRows = [1, 2])]
    public sealed record UnmappedPropertyRow(
        [property: Column("Region")] string Region,
        string NotAColumn);

    [Test]
    public async Task Model_ShouldRecordTheRangesItRead()
    {
        var (host, context) = await StartAsync("sheets model coverage");
        var model = context.Sheets().Open(_path).Model<SalesRow>();

        model.Verify();
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EqualTo(new[] { "Sales!A3:C4" }), "Verifying reads the whole data range.");

        _ = model.Rows;
        _ = model.Column(row => row.Amount);
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EquivalentTo(new[] { "Sales!A3:C4", "Sales!B3:B4" }));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_ShouldFailOnAnEmptyCellForANonNullableValueAndProjectOptionalDefaults()
    {
        var (host, context) = await StartAsync("sheets model empty");
        var sheet = context.Sheets().Open(_path);
        var strict = sheet.Model<LedgerRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => strict.Column(row => row.Amount));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("A4"));
            Assert.That(exception.Message, Does.Contain("Amount"));
        });

        var optional = sheet.Model<OptionalLedgerRow>().Column(row => row.Amount);
        Assert.That(optional.Values, Is.EqualTo(new decimal?[] { 1200m, 1200m, null }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
    }

    [Test]
    public async Task Model_VerifyShouldReportDuplicateNumericValues()
    {
        var (host, context) = await StartAsync("sheets unique numbers");
        var model = context.Sheets().Open(_path).Model<LedgerRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("repeats '1200'"));
    }

    [Test]
    public async Task Table_ShouldUseTypedHeaderValues()
    {
        var (host, context) = await StartAsync("sheets numeric header");
        var table = context.Sheets().Open(_path).Sheet("Years").Table(1);

        Assert.That(table.Headers[0], Is.EqualTo(new[] { "2024" }));
        table.Column("2024").Should.Be(["value"]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Range_ShouldMatchTypedValues()
    {
        var (host, context) = await StartAsync("sheets typed range");
        var sheet = context.Sheets().Open(_path).Sheet("Sales");

        sheet.Range("B3:C4").Should.Match(
        [
            ["1200", "12"],
            ["900", "9"]
        ]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Cell_ShouldNotBeBlank_ShouldFailOnABlankCell()
    {
        var (host, context) = await StartAsync("sheets negative blank");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => sheet.Cell("Z9").ShouldNot.BeBlank());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("not to be blank"));
            Assert.That(exception.Message, Does.Contain("<empty>"));
        });
    }

    [Test]
    public async Task Column_ShouldNotBe_ShouldPassOnAWrongSequence()
    {
        var (host, context) = await StartAsync("sheets negative column");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        table.Column("Amount").ShouldNot.Be(["999", "111"]);
        table.Column("Amount").ShouldNot.Be(["1200"]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Column_ShouldNotBe_ShouldFailOnAMatchingSequence()
    {
        var (host, context) = await StartAsync("sheets negative column failure");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => table.Column("Amount").ShouldNot.Be(["1200", "900"]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("not to match the expected 2 values"));
    }

    [Test]
    public async Task Range_ShouldNotHaveDimensions_ShouldPassOnAMismatch()
    {
        var (host, context) = await StartAsync("sheets negative dimensions");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        sheet.Range("A4:B5").ShouldNot.HaveDimensions(3, 3);
        sheet.Range("A4:B5").ShouldNot.Match([["Region"]]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Range_ShouldNot_ShouldFailOnAMatchingRange()
    {
        var (host, context) = await StartAsync("sheets negative range failure");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        var dimensions = Assert.Throws<SpreadsheetAssertionException>(
            () => sheet.Range("A4:B5").ShouldNot.HaveDimensions(2, 2));
        var values = Assert.Throws<SpreadsheetAssertionException>(
            () => sheet.Range("A4:B5").ShouldNot.Match(
            [
                ["Region", "Amount"],
                ["EMEA", "1200"]
            ]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(dimensions!));
        Assert.Multiple(() =>
        {
            Assert.That(dimensions!.Message, Does.Contain("not to have dimensions 2x2"));
            Assert.That(values!.Message, Does.Contain("not to match the expected 2x2 values"));
        });
    }

    [Test]
    public async Task Table_ShouldNotContainRow_ShouldFailOnAnExistingRow()
    {
        var (host, context) = await StartAsync("sheets negative row");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        table.ShouldNot.ContainRow("Region", "NOPE");
        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => table.ShouldNot.ContainRow("Region", "EMEA"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("not to contain a row where 'Region' is 'EMEA'"));
    }

    [Test]
    public async Task ModelColumn_ShouldNot_ShouldFailOnAMatchingSequence()
    {
        var (host, context) = await StartAsync("sheets negative model");
        var model = context.Sheets().Open(_path).Model<SalesRow>();

        model.Column(row => row.Amount).ShouldNot.Be([100m, 200m]);
        var sort = Assert.Throws<SpreadsheetAssertionException>(
            () => model.Column(row => row.Amount).ShouldNot.BeSortedBy(ascending: false));
        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => model.Column(row => row.Amount).ShouldNot.Be([1200m, 900m]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(sort!.Message, Does.Contain("not to be sorted descending"));
            Assert.That(exception!.Message, Does.Contain("not to match the expected 2 values"));
        });
    }

    [Test]
    public async Task NegativeAssertion_ShouldRecordTheFailedAssertOperation()
    {
        var (host, context) = await StartAsync("sheets negative trace");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => sheet.Cell("A1").ShouldNot.Be("Total"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        var operation = host.Trace.Snapshot().Tests.Single().Entries
            .Single(entry => entry.Kind == "assert.sheets");

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("not to be 'Total'"));
            Assert.That(operation.Outcome, Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(operation.Source, Is.EqualTo("ProtoTest.Sheets"));
            Assert.That(operation.Error, Is.Not.Null);
            Assert.That(operation.Error!.Message, Does.Contain("not to be 'Total'"));
            Assert.That(operation.Attributes["sheets.expected"], Is.EqualTo("be 'Total'"));
            Assert.That(operation.Attributes["sheets.actual"], Is.EqualTo("Total"));
        });
    }

    [Test]
    public async Task NegativeAssertions_ShouldStillRecordReads()
    {
        var (host, context) = await StartAsync("sheets negative coverage");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        table.Column("Amount").ShouldNot.Be(["999", "111"]);
        table.ShouldNot.ContainRow("Region", "NOPE");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(Coverage(context).Select(item => item.Identifier),
            Is.EquivalentTo(new[] { "Sales!B3:B4", "Sales!A3:C4" }));
    }

    [Test]
    public async Task NumberFormatWithQuotedCurrency_ShouldStayANumber()
    {
        var (host, context) = await StartAsync("sheets currency format");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");
        var quoted = sheet.Cell("D1");
        var bracketed = sheet.Cell("E1");
        var customDate = sheet.Cell("F1");

        Assert.Multiple(() =>
        {
            Assert.That(quoted.Number, Is.EqualTo(1234.5));
            Assert.That(quoted.Date, Is.Null);
            Assert.That(bracketed.Number, Is.EqualTo(987.65), "A bracketed literal like [$USD] is not a date token.");
            Assert.That(bracketed.Date, Is.Null);
            Assert.That(customDate.Date, Is.EqualTo(ReportDate), "A real date format still reads as a date.");
            Assert.That(customDate.Number, Is.Null);
        });
        quoted.Should.Be(1234.5);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_ShouldBindTypedRowsAndVerify()
    {
        var (host, context) = await StartAsync("sheets model");
        var model = context.Sheets().Open(_path).Model<SalesRow>();
        model.Verify();

        var emea = model.Row(row => row.Region == "EMEA");
        model.Column(row => row.Amount).Should.Be([1200m, 900m]);
        model.Column(row => row.Amount).ShouldAll(value => value > 0);
        model.Column(row => row.Amount).Should.BeSortedBy(ascending: false);
        Assert.Multiple(() =>
        {
            Assert.That(emea.Amount, Is.EqualTo(1200m));
            Assert.That(emea.Count, Is.EqualTo(12));
            Assert.That(model.Column(row => row.Amount).Values, Is.EqualTo(new decimal?[] { 1200m, 900m }));
            Assert.That(model.Rows.Select(row => row.Region), Is.EqualTo(new[] { "EMEA", "APAC" }));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_ShouldFailWithTheMissingColumn()
    {
        var (host, context) = await StartAsync("sheets model failure");
        var exception = Assert.Throws<SpreadsheetAssertionException>(() =>
        {
            _ = context.Sheets().Open(_path).Model<BrokenRow>();
        });

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("Nope"));
    }

    [Test]
    public async Task Model_ShouldMatchRowShape()
    {
        var (host, context) = await StartAsync("sheets shape");
        var model = context.Sheets().Open(_path).Model<SalesRow>();
        var emea = model.Row(row => row.Region == "EMEA");

        emea.ShouldMatchShape(new { Region = "EMEA", Amount = 1200m, Count = 12 });
        var mismatch = Assert.Throws<SpreadsheetAssertionException>(() =>
            emea.ShouldMatchShape(new { Region = "Nope" }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        Assert.That(mismatch!.Message, Does.Contain("Region"));
    }

    [Test]
    public async Task Verify_ShouldReportConstraintViolations()
    {
        var (host, context) = await StartAsync("sheets constraints");
        var model = context.Sheets().Open(_path).Model<StrictSalesRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("900"));
    }

    [Test]
    public async Task Open_ShouldAcceptNamedContent()
    {
        var (host, context) = await StartAsync("sheets content");
        var content = new NamedContent(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            File.ReadAllBytes(_path),
            "from-api.xlsx");

        var workbook = context.Sheets().Open(content);

        workbook.Sheet("Summary").Cell("B1").Should.Be(42);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_RowWhere_ShouldMatchATypedKeyCell()
    {
        var (host, context) = await StartAsync("sheets typed key");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);

        var first = table.RowWhere("Id", "100");

        Assert.That(first["Name"].Text, Is.EqualTo("first"));
        table.Should.ContainRow("Id", "100");
        table.Should.ContainRow("Id", "200");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_VerifyShouldSeparateTextAndNumberValuesForUnique()
    {
        var (host, context) = await StartAsync("sheets unique kinds");
        var model = context.Sheets().Open(_path).Model<MixedCodeRow>();

        model.Verify();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_VerifyShouldStillReportDuplicateTextValues()
    {
        var (host, context) = await StartAsync("sheets unique text duplicates");
        var model = context.Sheets().Open(_path).Model<DuplicateCodeRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("repeats '1200'"));
    }

    [Test]
    public async Task Rows_ShouldFailOnAnEmptyCellForANonNullableValue()
    {
        var (host, context) = await StartAsync("sheets rows empty");
        var model = context.Sheets().Open(_path).Model<LedgerRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => _ = model.Rows);

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("A4"));
            Assert.That(exception.Message, Does.Contain("Amount"));
        });
    }

    [Test]
    public async Task Model_VerifyShouldValidateMinAndMaxAgainstDateCells()
    {
        var (host, context) = await StartAsync("sheets date constraints");
        var workbook = context.Sheets().Open(_path);

        var minException = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Model<MinDateRow>().Verify());
        var maxException = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Model<MaxDateRow>().Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(minException!));
        Assert.Multiple(() =>
        {
            Assert.That(minException!.Message, Does.Contain("below the minimum"),
                "A date cell has no Number; Min must still compare its typed value.");
            Assert.That(maxException!.Message, Does.Contain("above the maximum"),
                "A date cell has no Number; Max must still compare its typed value.");
        });
    }

    [Test]
    public async Task Model_VerifyShouldValidatePatternAndOneOfAgainstNumericCells()
    {
        var (host, context) = await StartAsync("sheets numeric constraints");
        var model = context.Sheets().Open(_path).Model<NumericConstraintRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("does not match"),
                "A numeric cell has no Text; Pattern must still compare its rendered value.");
            Assert.That(exception.Message, Does.Contain("not one of"));
        });
    }

    [Test]
    public async Task Model_OptionalOnANonNullableValueType_ShouldFailWithGuidance()
    {
        var (host, context) = await StartAsync("sheets optional value");
        var exception = Assert.Throws<SpreadsheetAssertionException>(() =>
            _ = context.Sheets().Open(_path).Model<OptionalValueRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("nullable"));
    }

    [Test]
    public async Task ModelColumn_ShouldFailWithGuidanceForAnUnmappedProperty()
    {
        var (host, context) = await StartAsync("sheets unmapped property");
        var model = context.Sheets().Open(_path).Model<UnmappedPropertyRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Column(row => row.NotAColumn));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("[Column"));
    }

    [Test]
    public async Task Range_ShouldRejectAReversedReference()
    {
        var (host, context) = await StartAsync("sheets reversed range");
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
        var (host, context) = await StartAsync("sheets huge range");
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
        var (host, context) = await StartAsync("sheets invalid reference");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        Assert.Throws<FormatException>(() => sheet.Cell("NOT-A-CELL"));
        Assert.That(Coverage(context), Is.Empty, "A reference that failed to parse was never read.");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_RowRange_ShouldBeGuardedWhenThereAreNoDataRows()
    {
        var (host, context) = await StartAsync("sheets empty row range");
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
        var (host, context) = await StartAsync("sheets corrupt data");
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
        var (host, context) = await StartAsync("sheets date format ids");
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

            var (host, context) = await StartAsync("sheets 1904");
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
        var (host, context) = await StartAsync("sheets hidden excluded");
        var visible = context.Sheets().Open(_path);
        Assert.That(visible.Sheets.Any(sheet => sheet.Name == "Hidden"), Is.False,
            "Hidden sheets are excluded by default.");
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        var (includedHost, includedContext) = await StartAsync(
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

    private sealed record NamedContent(
        string? MediaType,
        ReadOnlyMemory<byte> Content,
        string? FileName) : IProtoBinaryContent;

    private static ProtoReportItem[] Coverage(ProtoExecutionContext context)
        => context.Services.GetServices<IProtoCollector>()
            .OfType<SheetsCoverageCollector>()
            .Single()
            .GetReportItems()
            .ToArray();

    private static async Task<(ProtoHost Host, ProtoExecutionContext Context)> StartAsync(
        string name,
        Action<SheetsOptions>? configure = null)
    {
        var builder = new ProtoHostBuilder();
        builder.AddSheets(configure);
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
