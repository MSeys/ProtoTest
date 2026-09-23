namespace ProtoTest.Sheets.Tests;

using System.Globalization;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Json;

public sealed partial class SheetsTests
{
    [Test]
    public async Task Open_ShouldReadTypedCellsAndFormulas()
    {
        var (host, context) = Start("sheets read");
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
        var (host, context) = Start("sheets missing");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Sheet("Missing"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("Summary"));
    }

    [Test]
    public async Task Range_ShouldMatchAndCountCoverage()
    {
        var (host, context) = Start("sheets range");
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
        var (host, context) = Start("sheets table");
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
        var (host, context) = Start("sheets table untouched");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        Assert.That(table.Headers, Has.Count.EqualTo(3));
        Assert.That(Coverage(context), Is.Empty, "Building the view is not reading the data.");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Table_ShouldRecordOnlyTheRangesActuallyRead()
    {
        var (host, context) = Start("sheets table coverage");
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
        var (host, context) = Start("sheets model coverage");
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
    public async Task Model_Column_ShouldAcceptACastExpression()
    {
        var (host, context) = Start("sheets model cast");
        var model = context.Sheets().Open(_path).Model<SalesRow>();

        var counts = model.Column(row => (long)row.Count);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(counts.Values, Is.EqualTo(new long?[] { 12, 9 }));
    }

    [Test]
    public async Task Model_ShouldFailOnAnEmptyCellForANonNullableValueAndProjectOptionalDefaults()
    {
        var (host, context) = Start("sheets model empty");
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
        var (host, context) = Start("sheets unique numbers");
        var model = context.Sheets().Open(_path).Model<LedgerRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("repeats '1200'"));
    }

    [Test]
    public async Task Table_ShouldUseTypedHeaderValues()
    {
        var (host, context) = Start("sheets numeric header");
        var table = context.Sheets().Open(_path).Sheet("Years").Table(1);

        Assert.That(table.Headers[0], Is.EqualTo(new[] { "2024" }));
        table.Column("2024").Should.Be(["value"]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Range_ShouldMatchTypedValues()
    {
        var (host, context) = Start("sheets typed range");
        var sheet = context.Sheets().Open(_path).Sheet("Sales");

        sheet.Range("B3:C4").Should.Match(
        [
            ["1200", "12"],
            ["900", "9"]
        ]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

}
