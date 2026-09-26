namespace ProtoTest.Analyzers.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

public sealed class MixedTestRegistrationTests
{
    [Test]
    public async Task NUnitProtoTestAttribute_ShouldReportWhenCombinedWithPlainTestAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                [Test]
                public void Journeys() { }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(
                diagnostics[0].GetMessage(),
                Does.Contain("ProtoTestAttribute").And.Contain("TestAttribute"));
        }
    }

    [Test]
    public async Task XunitProtoTestFactAttribute_ShouldReportWhenCombinedWithPlainFactAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using Xunit;
            using ProtoTest.Xunit3;

            public class Suite
            {
                [ProtoTestFact]
                [Fact]
                public void Journeys() { }
            }
            """);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
    }

    [Test]
    public async Task XunitProtoTestTheoryAttribute_ShouldReportWhenCombinedWithPlainTheoryAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using Xunit;
            using ProtoTest.Xunit3;

            public class Suite
            {
                [ProtoTestTheory]
                [Theory]
                [InlineData(1)]
                public void Journeys(int value) { }
            }
            """);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
    }

    [Test]
    public async Task MSTestProtoTestAttribute_ShouldReportWhenCombinedWithPlainTestMethodAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), FixtureStubs.MSTest + """

public class Suite
{
    [ProtoTest.MSTest.ProtoTest]
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
    public void Journeys() { }
}
""");

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
    }

    [Test]
    public async Task XunitV2ProtoTestFactAttribute_ShouldReportWhenCombinedWithPlainFactAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), FixtureStubs.XunitV2 + """

public class Suite
{
    [ProtoTest.Xunit.ProtoTestFact]
    [Xunit.Fact]
    public void Journeys() { }
}
""");

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
    }

    [Test]
    public async Task XunitV2ProtoTestTheoryAttribute_ShouldReportWhenCombinedWithPlainTheoryAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), FixtureStubs.XunitV2 + """

public class Suite
{
    [ProtoTest.Xunit.ProtoTestTheory]
    [Xunit.Theory]
    [Xunit.InlineData(1)]
    public void Journeys(int value) { }
}
""");

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0001" }));
    }

    [Test]
    public async Task Diagnostic_ShouldPointAtThePlainAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                [Test]
                public void Journeys() { }
            }
            """);

        var text = diagnostics[0].Location.SourceTree!.GetText();
        Assert.That(text.ToString(diagnostics[0].Location.SourceSpan), Is.EqualTo("Test"));
    }

    [Test]
    public async Task ProtoTestAttribute_ShouldStaySilentWithoutAPlainTestAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public void Journeys() { }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ProtoTestAttribute_ShouldStaySilentWithCaseData()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                [TestCase(1)]
                [TestCase(2)]
                public void Journeys(int value) { }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ProtoTestTheoryAttribute_ShouldStaySilentWithDataRows()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using Xunit;
            using ProtoTest.Xunit3;

            public class Suite
            {
                [ProtoTestTheory]
                [InlineData(1)]
                public void Journeys(int value) { }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task PlainTestAttribute_ShouldStaySilentWithoutAProtoTestAttribute()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new MixedTestRegistrationAnalyzer(), """
            using NUnit.Framework;

            public class Suite
            {
                [Test]
                public void Journeys() { }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }
}
