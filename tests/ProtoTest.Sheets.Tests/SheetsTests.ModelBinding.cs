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
    public async Task Model_ShouldBindTypedRowsAndVerify()
    {
        var (host, context) = Start("sheets model");
        var model = context.Sheets().Open(_path).Model<SalesRow>();
        model.Should.MatchModel();

        var emea = model.Row(row => row.Region == "EMEA");
        model.Column(row => row.Amount).Should.Be([1200m, 900m]);
        model.Column(row => row.Amount).Should.All(value => value > 0);
        model.Column(row => row.Amount).Should.BeSortedBy(ProtoSortDirection.Descending);
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
    public async Task Model_MatchHeaders_ShouldAcceptTheDeclaredColumnsInOrder()
    {
        var (host, context) = Start("sheets match headers");
        var model = context.Sheets().Open(_path).Model<SalesRow>();

        var returned = model.Should.MatchHeaders();
        var coverage = Coverage(context).Select(item => item.Identifier).ToArray();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(model));
            Assert.That(coverage, Does.Contain("Sales!A1:C2"), "matching the headers reads them");
        }
    }

    [Test]
    public async Task Model_MatchHeaders_ShouldFailOnAReorderedSheet()
    {
        var (host, context) = Start("sheets header order");
        var model = context.Sheets().Open(_path).Model<ReorderedRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchHeaders());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("column 1 is 'Id' where the model declares 'Name'"),
            "the sheet's order must be the model's declaration order");
    }

    [Test]
    public async Task Model_MatchHeaders_ShouldFailWhenTheSheetDeclaresMoreColumns()
    {
        var (host, context) = Start("sheets header count");
        var model = context.Sheets().Open(_path).Model<IdOnlyRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchHeaders());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("it has 2 columns and the model declares 1"));
    }

    [Test]
    public async Task Model_ShouldNotMatchHeaders_ShouldPassOnAReorderedSheet()
    {
        var (host, context) = Start("sheets negated headers");
        var workbook = context.Sheets().Open(_path);
        var reordered = workbook.Model<ReorderedRow>();
        var sales = workbook.Model<SalesRow>();

        var returned = reordered.ShouldNot.MatchHeaders();
        var exception = Assert.Throws<SpreadsheetAssertionException>(() => sales.ShouldNot.MatchHeaders());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(reordered));
            Assert.That(exception!.Message, Does.Contain("not to match SalesRow's declared columns in order"));
        }
    }

    [Test]
    public async Task Model_ShouldNotMatchModel_ShouldPassOnAViolation()
    {
        var (host, context) = Start("sheets negated model");
        var workbook = context.Sheets().Open(_path);
        var strict = workbook.Model<StrictSalesRow>();
        var matching = workbook.Model<SalesRow>();

        var returned = strict.ShouldNot.MatchModel();
        var exception = Assert.Throws<SpreadsheetAssertionException>(() => matching.ShouldNot.MatchModel());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(strict));
            Assert.That(exception!.Message, Does.Contain("not to match SalesRow but it did"));
        }
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
    public async Task TableRow_MatchShapeOnAFlowWithoutAmbientContext_ShouldUseTheOwningTablesContext()
    {
        // The row assertion uses the context its table carries, not the ambient
        // Proto.Context, so a row asserted from a helper flow still records and does not throw
        // "no active ProtoExecutionContext" on the flow it happens to run on.
        var (host, context) = Start("sheets row shape helper flow");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        // A fresh execution context carries no ambient Proto.Context; the row's table carries its own.
        // The task starts with no captured flow, and the suppression is undone on this same thread
        // because the block never awaits.
        using (ExecutionContext.SuppressFlow())
        {
            Task.Run(() => row.Should.MatchShape(new { Id = "100", Name = "first" }))
                .GetAwaiter()
                .GetResult();
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
            Has.Some.Matches<ProtoTraceEntry>(entry => entry.Kind == "assert.json.shape"),
            "the row assertion leaves its evidence under the owning test");
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
            Assert.That(mismatch!.Message, Does.StartWith("SalesRow — Shape mismatch failed with 1 error(s):"),
                "the model-row exception names the record type as its subject");
            Assert.That(mismatch.Message, Does.Contain("Region"));
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

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchModel());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("900"));
    }

    [Test]
    public async Task Should_Assertions_ShouldChainBackToTheirSubject()
    {
        var (host, context) = Start("sheets chaining");
        var workbook = context.Sheets().Open(_path);
        var summary = workbook.Sheet("Summary");
        var table = workbook.Sheet("Sales").Table(1, 2);
        var model = workbook.Model<SalesRow>();

        var cell = summary.Cell("A1");
        var range = summary.Range("A4:B5");
        var column = model.Column(row => row.Amount);

        var returnedCell = cell.Should.Be("Total").Should.BeText();
        var returnedRange = range.Should.HaveDimensions(2, 2)
            .Should.Match([["Region", "Amount"], ["EMEA", "1200"]]);
        var returnedTable = table.Should.ContainRow("Region", "EMEA").ShouldNot.ContainRow("Region", "NOPE");
        var returnedColumn = column.Should.Be([1200m, 900m]).Should.All(value => value > 0);
        var returnedModel = model.Should.MatchModel();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returnedCell, Is.SameAs(cell));
            Assert.That(returnedRange, Is.SameAs(range));
            Assert.That(returnedTable, Is.SameAs(table));
            Assert.That(returnedColumn, Is.SameAs(column));
            Assert.That(returnedModel, Is.SameAs(model));
        }
    }

    [Test]
    public async Task ShouldNot_All_ShouldPassWhenAValueDoesNotMatch()
    {
        var (host, context) = Start("sheets negated all");
        var amount = context.Sheets().Open(_path).Model<SalesRow>().Column(row => row.Amount);

        var returned = amount.ShouldNot.All(value => value > 1000);
        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => amount.ShouldNot.All(value => value >= 0));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(amount));
            Assert.That(exception!.Message, Does.Contain("not to hold only matching values"));
        }
    }

    [Test]
    public async Task Obsolete_VerifyAndShouldAll_ShouldStillDelegateToTheFacade()
    {
        var (host, context) = Start("sheets obsolete shims");
        var workbook = context.Sheets().Open(_path);
        var model = workbook.Model<SalesRow>();
        var strict = workbook.Model<StrictSalesRow>();

        // Intentional: pins the obsolete shims while they delegate to the facade; CS0618 is expected.
#pragma warning disable CS0618
        model.Verify();
        model.Column(row => row.Amount).ShouldAll(value => value > 0);
        var exception = Assert.Throws<SpreadsheetAssertionException>(() => strict.Verify());
#pragma warning restore CS0618

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("900"),
            "the obsolete Verify reports the same constraint violation as Should.MatchModel");
    }

    [Test]
    public async Task TableRow_ShouldMatchShapeAgainstLeafHeaderNames()
    {
        var (host, context) = Start("sheets table row shape");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        var returned = row.Should.MatchShape(new { Id = "100", Name = "first" });
        var mismatch = Assert.Throws<SpreadsheetAssertionException>(
            () => row.Should.MatchShape(new { Name = "nope" }));
        var coverage = Coverage(context).Select(item => item.Identifier).ToArray();

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(row));
            Assert.That(mismatch!.Message, Does.StartWith("Keys!A2:B2 — Shape mismatch failed with 1 error(s):"),
                "the failure names the row it was made against");
            Assert.That(mismatch.Message, Does.Contain("Name"));
            Assert.That(mismatch.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch details stay inspectable");
            Assert.That(coverage, Is.EquivalentTo(new[] { "Keys!A2:B3", "Keys!A2:B2" }),
                "the RowWhere data range and the shape's own row read both count as coverage");
            Assert.That(host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "assert.json.shape" && entry.Outcome == ProtoTraceOutcome.Failed),
                "a table row shape mismatch leaves the same traced evidence as a model row");
        }
    }

    [Test]
    public async Task TableRow_MatchShape_Exact_ShouldRejectColumnsTheShapeDoesNotMention()
    {
        var (host, context) = Start("sheets table row exact shape");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        row.Should.MatchShape(new { Id = "100" });

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => row.Should.MatchShape(new { Id = "100" }, exact: true));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.StartWith("Keys!A2:B2 — Shape mismatch failed with 1 error(s):"));
            Assert.That(exception.Message, Does.Contain("$.Name"));
            Assert.That(exception.Message, Does.Contain("Property was not mentioned in the expected shape."));
            Assert.That(exception.InnerException, Is.TypeOf<JsonShapeMismatchException>(),
                "the shared mismatch details stay inspectable");
        }
    }

    [Test]
    public async Task TableRow_MatchShape_Exact_ShouldPassWhenEveryColumnIsMentioned()
    {
        var (host, context) = Start("sheets table row exact success");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        var returned = row.Should.MatchShape(new { Id = "100", Name = "first" }, exact: true);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(returned, Is.SameAs(row));
    }

    [Test]
    public async Task TableRow_MatchShape_Exact_ShouldTreatAValueConstraintAsMentioningItsSubtree()
    {
        var (host, context) = Start("sheets table row exact constraint");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        row.Should.MatchShape(
            new { Id = JsonValue.NotNull(), Name = JsonValue.StringContaining("fir") },
            exact: true);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task TableRow_MatchShape_Exact_ShouldKeepIgnoringExtraColumnsWithoutExact()
    {
        var (host, context) = Start("sheets table row partial shape");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        Assert.DoesNotThrow(() => row.Should.MatchShape(new { Id = "100" }));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task ModelRow_ShouldMatchShape_Exact_ShouldRejectPropertiesTheShapeDoesNotMention()
    {
        var (host, context) = Start("sheets model row exact shape");
        var model = context.Sheets().Open(_path).Model<SalesRow>();
        var emea = model.Row(row => row.Region == "EMEA");

        var returned = emea.ShouldMatchShape(new { Region = "EMEA", Amount = 1200m, Count = 12 }, exact: true);

        var mismatch = Assert.Throws<SpreadsheetAssertionException>(
            () => emea.ShouldMatchShape(new { Region = "EMEA" }, exact: true));

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(emea));
            Assert.That(mismatch!.Message, Does.StartWith("SalesRow — Shape mismatch failed with 2 error(s):"));
            Assert.That(mismatch.Message, Does.Contain("$.Amount"));
            Assert.That(mismatch.Message, Does.Contain("$.Count"));
            Assert.That(mismatch.InnerException, Is.TypeOf<JsonShapeMismatchException>());
        }
    }

    [Test]
    public async Task TableRow_ShouldMatchShape_ShouldMapAnEmptyCellToNull()
    {
        var (host, context) = Start("sheets table row empty shape");
        var table = context.Sheets().Open(_path).Sheet("Ledger").Table(1);
        var row = table.Rows[2];

        var returned = row.Should.MatchShape(new { Amount = (string?)null, Note = "missing amount" });
        var amount = row["Amount"];

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(row));
            Assert.That(amount.IsEmpty, Is.True, "the shape was compared against the empty cell");
        }
    }

    [Test]
    public async Task TableRow_ShouldMatchShape_ShouldFailOnDuplicateLeafHeaders()
    {
        var (host, context) = Start("sheets ambiguous row shape");
        var table = context.Sheets().Open(_path).Sheet("Groups").Table(1, 2);
        var row = table.Rows[0];

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => row.Should.MatchShape(new { Amount = "1200" }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("more than one column"));
    }

    [Test]
    public async Task TableRow_ShouldMatchShape_ShouldFailOnCaseVariantLeafHeaders()
    {
        var (host, context) = Start("sheets case-ambiguous row shape");
        var table = context.Sheets().Open(_path).Sheet("CaseGroups").Table(1, 2);
        var row = table.Rows[0];

        var exception = Assert.Throws<SpreadsheetAssertionException>(
            () => row.Should.MatchShape(new { Amount = "1200" }));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("more than one column"),
            "the shape lookup is case-insensitive, so case variants collide too");
    }

    [Test]
    public async Task Obsolete_TableRowShouldMatchShape_ShouldStillDelegateToTheFacade()
    {
        var (host, context) = Start("sheets obsolete row shape");
        var table = context.Sheets().Open(_path).Sheet("Keys").Table(1);
        var row = table.RowWhere("Id", "100");

        // Intentional: pins the obsolete shim while it delegates to the facade; CS0618 is expected.
#pragma warning disable CS0618
        var returned = row.ShouldMatchShape(new { Id = "100", Name = "first" });
        var mismatch = Assert.Throws<SpreadsheetAssertionException>(
            () => row.ShouldMatchShape(new { Name = "nope" }));
#pragma warning restore CS0618
        var exact = Assert.Throws<SpreadsheetAssertionException>(
            () => row.Should.MatchShape(new { Id = "100" }, exact: true));

        await host.CompleteTestAsync(ProtoTestResult.Failed(mismatch!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(row));
            Assert.That(mismatch!.Message, Does.StartWith("Keys!A2:B2 — Shape mismatch"));
            Assert.That(exact!.Message, Does.Contain("$.Name"));
        }
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

        model.Should.MatchModel();

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task Model_VerifyShouldStillReportDuplicateTextValues()
    {
        var (host, context) = Start("sheets unique text duplicates");
        var model = context.Sheets().Open(_path).Model<DuplicateCodeRow>();

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchModel());

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

        var minException = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Model<MinDateRow>().Should.MatchModel());
        var maxException = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Model<MaxDateRow>().Should.MatchModel());

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

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchModel());

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

    [Test]
    public async Task Model_ShouldRunTheRecordsConstructorGuardWhenProjecting()
    {
        var (host, context) = Start("sheets constructor guard");
        var model = context.Sheets().Open(_path).Model<GuardedRow>();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _ = model.Rows);

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("Value must be positive"),
            "the projection failed with the guard's own message, not a reflection wrapper");
    }

    [Test]
    public async Task Model_ShouldRejectAConstructorParameterThatNoColumnMaps()
    {
        var (host, context) = Start("sheets unmapped constructor parameter");
        var exception = Assert.Throws<SpreadsheetAssertionException>(() =>
            _ = context.Sheets().Open(_path).Model<UnmappedConstructorRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("NotAColumn"), "the failure names the parameter");
            Assert.That(exception.Message, Does.Contain("[Column"));
        });
    }

}
