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
    public async Task Model_ShouldBindTypedRowsAndVerify()
    {
        var (host, context) = Start("sheets model");
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
        var (host, context) = Start("sheets model failure");
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
        var (host, context) = Start("sheets shape");
        var model = context.Sheets().Open(_path).Model<SalesRow>();
        var emea = model.Row(row => row.Region == "EMEA");

        emea.ShouldMatchShape(new { Region = "EMEA", Amount = 1200m, Count = 12 });
        var mismatch = Assert.Throws<SpreadsheetAssertionException>(() =>
            emea.ShouldMatchShape(new { Region = "Nope" }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        Assert.Multiple(() =>
        {
            Assert.That(mismatch!.Message, Does.Contain("Region"));
            Assert.That(mismatch.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch details stay inspectable");
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "assert.json.shape" && entry.Outcome == ProtoTraceOutcome.Failed),
                "a row shape mismatch leaves the same traced evidence as a protocol assertion");
        });
    }

    [Test]
    public async Task Verify_ShouldReportConstraintViolations()
    {
        var (host, context) = Start("sheets constraints");
        var model = context.Sheets().Open(_path).Model<StrictSalesRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("900"));
    }

    [Test]
    public async Task Open_ShouldAcceptNamedContent()
    {
        var (host, context) = Start("sheets content");
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
        var (host, context) = Start("sheets typed key");
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
        var (host, context) = Start("sheets unique kinds");
        var model = context.Sheets().Open(_path).Model<MixedCodeRow>();

        model.Verify();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_VerifyShouldStillReportDuplicateTextValues()
    {
        var (host, context) = Start("sheets unique text duplicates");
        var model = context.Sheets().Open(_path).Model<DuplicateCodeRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Verify());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("repeats '1200'"));
    }

    [Test]
    public async Task Rows_ShouldFailOnAnEmptyCellForANonNullableValue()
    {
        var (host, context) = Start("sheets rows empty");
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
        var (host, context) = Start("sheets date constraints");
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
        var (host, context) = Start("sheets numeric constraints");
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
        var (host, context) = Start("sheets optional value");
        var exception = Assert.Throws<SpreadsheetAssertionException>(() =>
            _ = context.Sheets().Open(_path).Model<OptionalValueRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("nullable"));
    }

    [Test]
    public async Task ModelColumn_ShouldFailWithGuidanceForAnUnmappedProperty()
    {
        var (host, context) = Start("sheets unmapped property");
        var model = context.Sheets().Open(_path).Model<UnmappedPropertyRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Column(row => row.NotAColumn));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("[Column"));
    }

}
