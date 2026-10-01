namespace ProtoTest.Analyzers.Tests;

using System.Linq;
using System.Threading.Tasks;

public sealed class TestShortcutTests
{
    [Test]
    public async Task ProtoTestWithFixedWaits_ShouldReportEachWait()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public async Task Waits()
                {
                    await Task.Delay(500);
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    Thread.Sleep(100);
                    void Local() => Thread.Sleep(10);
                    Func<Task> lambda = () => Task.Delay(20);
                    Local();
                    await lambda();
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.All.EqualTo("PT0003"));
            Assert.That(diagnostics, Has.Length.EqualTo(5), "the local function and the lambda report through the test");
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("'Waits' waits a fixed time with Task.Delay").And.Contain("Proto.Context.Clock"));
        }
    }

    [Test]
    public async Task ZeroAndInfiniteDelays_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), """
            using System.Threading;
            using System.Threading.Tasks;
            using ProtoTest.NUnit;

            public class Suite
            {
                [ProtoTest]
                public async Task Yields(CancellationToken token)
                {
                    await Task.Delay(0);
                    Thread.Sleep(0);
                    await Task.Delay(Timeout.Infinite, token);
                }
            }
            """);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ProtoTestCreatingAnHttpClient_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), """
            using System.Net.Http;
            using System.Threading.Tasks;
            using ProtoTest.Xunit3;

            public class Suite
            {
                [ProtoTestFact]
                public async Task CallsByHand()
                {
                    using var client = new HttpClient();
                    await client.GetAsync("http://localhost/api/orders");
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0004" }));
            Assert.That(diagnostics[0].GetMessage(), Does.Contain("'CallsByHand' creates an HttpClient").And.Contain("Proto.Context.Rest()"));
        }
    }

    [Test]
    public async Task PlainTestsUnderAutoWrap_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), """
            using System.Threading.Tasks;
            using NUnit.Framework;
            using ProtoTest.NUnit;

            [assembly: ProtoTestAutoWrap]

            public class Suite
            {
                [Test]
                public Task Wrapped() => Task.Delay(100);
            }
            """);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0003" }));
    }

    [Test]
    public async Task TUnitTestsRunByTheProtoTestExecutor_ShouldReport()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), FixtureStubs.TUnitWithExecutor + """

public class Suite
{
    [TUnit.Core.Test]
    public System.Threading.Tasks.Task Waits() => System.Threading.Tasks.Task.Delay(100);
}
""");

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(new[] { "PT0003" }));
    }

    [Test]
    public async Task PlainTestsHelpersAndHooks_ShouldStaySilent()
    {
        var diagnostics = await AnalyzerTestFixture.GetDiagnosticsAsync(new TestShortcutAnalyzer(), FixtureStubs.TUnit + """

public class Suite
{
    [NUnit.Framework.Test]
    public System.Threading.Tasks.Task PlainNUnit() => System.Threading.Tasks.Task.Delay(100);

    [TUnit.Core.Test]
    public System.Threading.Tasks.Task TUnitWithoutTheExecutor() => System.Threading.Tasks.Task.Delay(100);

    [ProtoTest.NUnit.ProtoTest]
    public System.Threading.Tasks.Task CallsAHelper() => Helper();

    private static System.Threading.Tasks.Task Helper()
    {
        using var client = new System.Net.Http.HttpClient();
        return System.Threading.Tasks.Task.Delay(100);
    }
}
""");

        Assert.That(diagnostics, Is.Empty, "only a method that runs in the lifecycle is checked, not what it calls");
    }
}
