namespace ProtoTest.TUnit.Tests;

using global::TUnit.Assertions.Enums;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

/// <summary>
/// Tests the lifecycle execution order of class-level and method-level <see cref="ProtoAttribute"/> instances in TUnit.
/// </summary>
[Tracking("Class", Order = 10)]
public class ProtoAttributeLifecycleTests
{
    [Test]
    [Tracking("Method", Order = 20)]
    public async Task ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        // Note: The AfterTest hooks execute after this test body completes,
        // so we check the Before execution log here within the test body.
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        await Assert.That(Proto.Context.Resolve<ExecutionLogState>().Log)
            .IsEquivalentTo(expectedBeforeSequence, CollectionOrdering.Matching);
    }
}
