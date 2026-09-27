namespace ProtoTest.Sheets.Tests;

using ProtoTest.Core;

public sealed partial class SheetsTests
{
    [Test]
    public async Task KeyValueModel_ShouldReadValuesByPropertyAndAssertThem()
    {
        var (host, context) = Start("sheets key value read");
        var model = context.Sheets().Open(_path).KeyValueModel<KeyValueRow>();

        var total = model.Column(row => row.Total);
        var returned = total.Should.Be(123.45m);
        model.Column(row => row.Count).Should.Be(3);
        model.Column(row => row.Currency).Should.Be("EUR");
        model.Column(row => row.Note).Should.Be((string?)null);
        var exception = Assert.Throws<SpreadsheetAssertionException>(() => total.ShouldNot.Be(123.45m));
        var coverage = Coverage(context).Select(item => item.Identifier).ToArray();

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(total));
            Assert.That(total.Label, Is.EqualTo("Total"));
            Assert.That(total.Value, Is.EqualTo(123.45m));
            Assert.That(model.Labels, Is.EqualTo(new[] { "Total", "Count", "Currency", "Note" }));
            Assert.That(exception!.Message, Does.Contain("not to be 123.45"));
            Assert.That(coverage, Does.Contain("KeyValues!A1:B4"), "reading an entry covers the block");
        }
    }

    [Test]
    public async Task KeyValueModel_MatchModel_ShouldCheckEveryDeclaredLabel()
    {
        var (host, context) = Start("sheets key value model");
        var workbook = context.Sheets().Open(_path);
        var model = workbook.KeyValueModel<KeyValueRow>();

        var returned = model.Should.MatchModel();
        var constrained = workbook.KeyValueModel<ConstrainedKeyValueRow>();
        var exception = Assert.Throws<SpreadsheetAssertionException>(() => constrained.Should.MatchModel());
        var negated = constrained.ShouldNot.MatchModel();

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(returned, Is.SameAs(model));
            Assert.That(exception!.Message, Does.Contain("below the minimum 1000"));
            Assert.That(negated, Is.SameAs(constrained));
            Assert.That(
                host.Trace.Snapshot().Tests.Single().Entries,
                Has.Some.Matches<ProtoTraceEntry>(entry =>
                    entry.Kind == "sheets.model"
                    && entry.Attributes.TryGetValue("sheets.labels", out var labels)
                    && labels == "4"),
                "the key-value model records the labels it verified");
        }
    }

    [Test]
    public async Task KeyValueModel_ShouldFailNamingTheLabelsTheSheetCarries()
    {
        var (host, context) = Start("sheets key value missing label");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.KeyValueModel<MissingLabelRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("no label 'Nope'"));
            Assert.That(exception.Message, Does.Contain("Total"),
                "the failure names the labels the sheet carries");
        }
    }

    [Test]
    public async Task KeyValueModel_ShouldFailOnAValueThatDoesNotConvert()
    {
        var (host, context) = Start("sheets key value wrong type");
        var model = context.Sheets().Open(_path).KeyValueModel<WrongValueTypeRow>();

        var read = Assert.Throws<FormatException>(() => model.Column(row => row.Total));
        var verify = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchModel());

        await host.CompleteTestAsync(ProtoTestResult.Failed(verify!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read!.Message, Does.Contain("at B1"));
            Assert.That(read.Message, Does.Contain("cannot be converted to Int32"));
            Assert.That(verify!.Message, Does.Contain("'Total' is not a Int32 at B1"));
        }
    }

    [Test]
    public async Task KeyValueModel_ShouldFailOnAnEmptyValueForANonNullableProperty()
    {
        var (host, context) = Start("sheets key value empty");
        var model = context.Sheets().Open(_path).KeyValueModel<EmptyValueRow>();

        var read = Assert.Throws<SpreadsheetAssertionException>(() => model.Column(row => row.Note));
        var verify = Assert.Throws<SpreadsheetAssertionException>(() => model.Should.MatchModel());

        await host.CompleteTestAsync(ProtoTestResult.Failed(verify!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(read!.Message, Does.Contain("'Note' is empty at B4"));
            Assert.That(verify!.Message, Does.Contain("'Note' is empty at B4"));
        }
    }

    [Test]
    public async Task KeyValueModel_ShouldFailOnADuplicateLabel()
    {
        var (host, context) = Start("sheets key value duplicate label");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.KeyValueModel<DuplicateLabelRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.Message, Does.Contain("'Total' more than once"));
            Assert.That(exception.Message, Does.Contain("rows 1 and 2"));
        }
    }

    [Test]
    public async Task KeyValueModel_ShouldRejectALabelPathWithSegments()
    {
        var (host, context) = Start("sheets key value label path");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.KeyValueModel<LabelPathRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("a key-value label is a single text"));
    }

    [Test]
    public async Task KeyValueModel_ShouldRejectUniqueOnAKeyValueModel()
    {
        var (host, context) = Start("sheets key value unique");
        var workbook = context.Sheets().Open(_path);

        var exception = Assert.Throws<SpreadsheetAssertionException>(() => workbook.KeyValueModel<UniqueLabelRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.Message, Does.Contain("marked Unique"));
    }

    [Test]
    public async Task KeyValueModel_ShouldMatchTheKindTheSheetDeclares()
    {
        var (host, context) = Start("sheets key value kind");
        var workbook = context.Sheets().Open(_path);

        var tableKind = Assert.Throws<SpreadsheetAssertionException>(() => workbook.KeyValueModel<SalesRow>());
        var keyValueKind = Assert.Throws<SpreadsheetAssertionException>(() => workbook.Model<KeyValueRow>());

        await host.CompleteTestAsync(ProtoTestResult.Failed(tableKind!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tableKind!.Message, Does.Contain("read it with Model<SalesRow>()"));
            Assert.That(keyValueKind!.Message, Does.Contain("read it with KeyValueModel<KeyValueRow>()"));
        }
    }

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record KeyValueRow(
        [property: Column("Total")] decimal Total,
        [property: Column("Count")] int Count,
        [property: Column("Currency")] string Currency,
        [property: Column("Note", Optional = true)] string? Note);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record MissingLabelRow(
        [property: Column("Nope")] string Nope);

    [Sheet("DuplicateLabels", Kind = ProtoSheetKind.KeyValue)]
    public sealed record DuplicateLabelRow(
        [property: Column("Total")] decimal Total);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record WrongValueTypeRow(
        [property: Column("Total")] int Total);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record ConstrainedKeyValueRow(
        [property: Column("Total", Min = 1000)] decimal Total);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record LabelPathRow(
        [property: Column("Total", "Amount")] decimal Total);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record UniqueLabelRow(
        [property: Column("Total", Unique = true)] decimal Total);

    [Sheet("KeyValues", Kind = ProtoSheetKind.KeyValue)]
    public sealed record EmptyValueRow(
        [property: Column("Note")] decimal Note);
}
