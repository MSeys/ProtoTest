namespace ProtoTest.Analyzers.Tests;

using System.Linq;
using System.Threading.Tasks;

/// <summary>
/// PT0003: a <c>[Column]</c> on a key-value sheet model is the deprecated spelling; the analyzer
/// reports it only where the model declares <c>Kind = ProtoSheetKind.KeyValue</c>.
/// </summary>
public sealed class KeyValueSheetColumnTests
{
    [Test]
    public async Task ColumnOnAKeyValueModel_ShouldReportTheLabelMapping()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new KeyValueSheetColumnAnalyzer(), """
            using ProtoTest.Sheets;

            namespace Fixture
            {
                [Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]
                public sealed record Summary([property: Column("Total")] decimal Total);
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0003" }));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("'Total'"));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("[Label"));
        }
    }

    [Test]
    public async Task LabelOnAKeyValueModel_ShouldBeClean()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new KeyValueSheetColumnAnalyzer(), """
            using ProtoTest.Sheets;

            namespace Fixture
            {
                [Sheet("Summary", Kind = ProtoSheetKind.KeyValue)]
                public sealed record Summary([property: Label("Total")] decimal Total);
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ColumnOnATableModel_ShouldBeClean()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new KeyValueSheetColumnAnalyzer(), """
            using ProtoTest.Sheets;

            namespace Fixture
            {
                [Sheet("Sales", HeaderRows = [1, 2])]
                public sealed record Sale([property: Column("Region")] string Region);

                [Sheet("Sales", Kind = ProtoSheetKind.Table)]
                public sealed record OtherSale([property: Column("Region")] string Region);
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ColumnOnAClassWithoutASheet_ShouldBeClean()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new KeyValueSheetColumnAnalyzer(), """
            using ProtoTest.Sheets;

            namespace Fixture
            {
                public sealed record Unbound([property: Column("Region")] string Region);
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }
}
