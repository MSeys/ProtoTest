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
    public async Task Cell_ShouldNotBeBlank_ShouldFailOnABlankCell()
    {
        var (host, context) = Start("sheets negative blank");
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
        var (host, context) = Start("sheets negative column");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        table.Column("Amount").ShouldNot.Be(["999", "111"]);
        table.Column("Amount").ShouldNot.Be(["1200"]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Column_ShouldNotBe_ShouldFailOnAMatchingSequence()
    {
        var (host, context) = Start("sheets negative column failure");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => table.Column("Amount").ShouldNot.Be(["1200", "900"]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("not to match the expected 2 values"));
    }

    [Test]
    public async Task Range_ShouldNotHaveDimensions_ShouldPassOnAMismatch()
    {
        var (host, context) = Start("sheets negative dimensions");
        var sheet = context.Sheets().Open(_path).Sheet("Summary");

        sheet.Range("A4:B5").ShouldNot.HaveDimensions(3, 3);
        sheet.Range("A4:B5").ShouldNot.Match([["Region"]]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Range_ShouldNot_ShouldFailOnAMatchingRange()
    {
        var (host, context) = Start("sheets negative range failure");
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
        var (host, context) = Start("sheets negative row");
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
        var (host, context) = Start("sheets negative model");
        var model = context.Sheets().Open(_path).Model<SalesRow>();

        model.Column(row => row.Amount).ShouldNot.Be([100m, 200m]);
        var sort = Assert.Throws<SpreadsheetAssertionException>(
            () => model.Column(row => row.Amount).ShouldNot.BeSortedBy(ProtoSortDirection.Descending));
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
        var (host, context) = Start("sheets negative trace");
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
        var (host, context) = Start("sheets negative coverage");
        var table = context.Sheets().Open(_path).Sheet("Sales").Table(1, 2);

        table.Column("Amount").ShouldNot.Be(["999", "111"]);
        table.ShouldNot.ContainRow("Region", "NOPE");

        // Coverage must be read before completion: completing the test disposes the test scope.
        var coverage = Coverage(context).Select(item => item.Identifier).ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(coverage, Is.EquivalentTo(new[] { "Sales!B3:B4", "Sales!A3:C4" }));
    }

    [Test]
    public async Task NumberFormatWithQuotedCurrency_ShouldStayANumber()
    {
        var (host, context) = Start("sheets currency format");
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

}
