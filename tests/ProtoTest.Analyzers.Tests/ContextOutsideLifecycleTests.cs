namespace ProtoTest.Analyzers.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;

public sealed class ContextOutsideLifecycleTests
{
    [Test]
    public async Task PlainNUnitTestReadingContext_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.Core;

            public class Suite
            {
                [Test]
                public void PlainTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0002" }));
            Assert.That(diagnostics[0].Severity, Is.EqualTo(DiagnosticSeverity.Warning));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("TestAttribute"));
        }

        var text = diagnostics[0].Location.SourceTree!.GetText();
        Assert.That(text.ToString(diagnostics[0].Location.SourceSpan), Is.EqualTo("Proto.Context"));
    }

    [Test]
    public async Task PlainXunitFactReadingContext_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using Xunit;
            using ProtoTest.Core;

            public class Suite
            {
                [Fact]
                public void PlainTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0002" }));
    }

    [Test]
    public async Task PlainMSTestReadingContext_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), FixtureStubs.MSTest + """

public class Suite
{
    [Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
    public void PlainTest()
    {
        _ = ProtoTest.Core.Proto.Context;
    }
}
""");

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0002" }));
    }

    [Test]
    public async Task PlainTestReadingContextThroughRestAccessor_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.Core;
            using ProtoTest.Rest;

            public class Suite
            {
                [Test]
                public void PlainTest()
                {
                    Proto.Context.Rest();
                }
            }
            """);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0002" }));
    }

    [Test]
    public async Task PlainTestInMixedClassReadingContext_ShouldReportOnlyOnThePlainTest()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.Core;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public void LifecycleTest()
                {
                    _ = Proto.Context;
                }

                [Test]
                public void PlainTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0002" }));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("PlainTest"));
        }
    }

    [Test]
    public async Task ProtoTestAttributeReadingContext_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using ProtoTest.Core;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public void LifecycleTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ProtoTestFactAttributeReadingContext_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using ProtoTest.Core;
            using ProtoTest.Xunit3;

            public class Suite
            {
                [ProtoTestFact]
                public void LifecycleTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task HelperReadingContext_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using ProtoTest.Core;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public void LifecycleTest()
                {
                    Helper();
                }

                private static void Helper()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task PlainTestWithoutContext_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using NUnit.Framework;

            public class Suite
            {
                [Test]
                public void PlainTest()
                {
                    _ = 1;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task TUnitTestReadingContext_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), FixtureStubs.TUnit + """

public class Suite
{
    [TUnit.Core.Test]
    public void TUnitTest()
    {
        _ = ProtoTest.Core.Proto.Context;
    }
}
""");

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task PlainNUnitTestUnderAutoWrap_ShouldNotReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using NUnit.Framework;
            using ProtoTest.Core;
            using ProtoTest.NUnit;

            [assembly: ProtoTestAutoWrap]

            public class Suite
            {
                [Test]
                public void WrappedTest()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty, "auto-wrap runs every plain [Test] inside the lifecycle");
    }

    [Test]
    public async Task PlainXunit3FactUnderAutoWrap_ShouldNotReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new ContextWithoutLifecycleAnalyzer(), """
            using Xunit;
            using ProtoTest.Core;

            [assembly: ProtoTest.Xunit3.ProtoTestAutoWrap]

            public class Suite
            {
                [Fact]
                public void WrappedFact()
                {
                    _ = Proto.Context;
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty, "auto-wrap runs every plain [Fact] inside the lifecycle");
    }
}
