namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// Stage 4 (Audit 3, finding E1): the lifecycle is a command wrapper outside NUnit's setup and teardown,
/// so the skip decision precedes <c>[SetUp]</c> and <c>[TearDown]</c> runs inside the lifecycle.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class LifecycleBoundaryTests
{
    private static int _setUpRuns;

    [SetUp]
    public void BeforeEach() => _setUpRuns++;

    [TearDown]
    public void AfterEach()
        => Assert.That(
            TryGetContext(out _),
            Is.True,
            "[TearDown] runs inside the lifecycle");

    [Test]
    [Order(1)]
    [ProtoTest]
    [RequiresCapability("not-composed", Reason = "the boundary fixture proves the skip path")]
    public void SkippedTest_ShouldNotRunItsSetUp()
        => throw new InvalidOperationException("A skipped test must not run its body.");

    [Test]
    [Order(2)]
    [ProtoTest]
    public void SkipDecision_ShouldPrecedeSetUp()
        => Assert.That(
            _setUpRuns,
            Is.EqualTo(1),
            "the skipped test's [SetUp] must not run; only this test's has");

    private static bool TryGetContext(out ProtoExecutionContext? context)
    {
        try
        {
            context = Proto.Context;
            return true;
        }
        catch (InvalidOperationException)
        {
            context = null;
            return false;
        }
    }
}
