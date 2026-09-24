namespace ProtoTest.Sheets.Tests;

using DocumentFormat.OpenXml.Spreadsheet;
using ProtoTest.Sheets.Internal;

[TestFixture]
public sealed class SheetDateFormatTests
{
    [TestCase(14, true)]
    [TestCase(22, true)]
    [TestCase(27, true)]
    [TestCase(36, true)]
    [TestCase(45, true)]
    [TestCase(58, true)]
    [TestCase(0, false)]
    [TestCase(13, false)]
    [TestCase(23, false)]
    [TestCase(37, false)]
    [TestCase(44, false)]
    [TestCase(49, false)]
    [TestCase(59, false)]
    public void BuiltInIds_ShouldResolveByTheirDocumentedRange(int formatId, bool isDate)
    {
        var formats = SheetDateFormat.FromStylesheet(new Stylesheet(new CellFormats(
            new CellFormat { NumberFormatId = (uint)formatId })));

        Assert.That(formats.IsDateStyle(0), Is.EqualTo(isDate));
    }

    [Test]
    public void StyleIndex_ShouldResolveWithinTheTableAndRejectMissingIndexes()
    {
        var formats = SheetDateFormat.FromStylesheet(new Stylesheet(new CellFormats(
            new CellFormat { NumberFormatId = 14 })));

        Assert.Multiple(() =>
        {
            Assert.That(formats.IsDateStyle(null), Is.False);
            Assert.That(formats.IsDateStyle(1), Is.False, "a style index past the table is not a date");
            Assert.That(SheetDateFormat.FromStylesheet(null).IsDateStyle(0), Is.False,
                "a workbook without a stylesheet has no date cells");
        });
    }

    [Test]
    public void CustomFormat_ShouldResolveByItsCode()
    {
        var formats = SheetDateFormat.FromStylesheet(new Stylesheet(
            new NumberingFormats(
                new NumberingFormat { NumberFormatId = 164, FormatCode = "yyyy-mm-dd" },
                new NumberingFormat { NumberFormatId = 165, FormatCode = "#,##0.00" },
                new NumberingFormat { NumberFormatId = 166 }),
            new CellFormats(
                new CellFormat { NumberFormatId = 164 },
                new CellFormat { NumberFormatId = 165 },
                new CellFormat { NumberFormatId = 166 })));

        Assert.Multiple(() =>
        {
            Assert.That(formats.IsDateStyle(0), Is.True, "the custom date code is a date");
            Assert.That(formats.IsDateStyle(1), Is.False, "the custom number code is not");
            Assert.That(formats.IsDateStyle(2), Is.False, "a custom format without a code is not");
        });
    }

    [TestCase("yyyy-mm-dd", true)]
    [TestCase("d-mmm-yy", true)]
    [TestCase("h:mm:ss", true)]
    [TestCase("[h]:mm:ss", true)]
    [TestCase("[$-409]d-mmm-yy", true)]
    [TestCase("#,##0.00", false)]
    [TestCase("#,##0.00\" USD\"", false)]
    [TestCase("[$USD] #,##0.00", false)]
    [TestCase("\\y", false)]
    [TestCase("\"day\" 0", false)]
    [TestCase("General", false)]
    public void Code_ShouldCarryADateTokenOnlyOutsideLiterals(string code, bool isDate)
        => Assert.That(SheetDateFormat.IsDateCode(code), Is.EqualTo(isDate));
}
