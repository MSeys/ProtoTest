namespace ProtoTest.TUnit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using global::TUnit.Assertions.Enums;

/// <summary>
/// Tests the lifecycle execution order of class-level and method-level <see cref="ProtoAttribute"/> instances in TUnit.
/// </summary>
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{
    [Test]
    [Tracking("MethodLevel", Order = 2)]
    public async Task ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        // Note: The AfterTest hooks execute after this test body completes,
        // so we check the Before execution log here within the test body.
        string[] expectedBeforeSequence = [.. AdapterTestSupport.ExpectedBeforeSequence, "TestExecution"];
        await Assert.That(Proto.Context.Resolve<ExecutionLogState>().Log)
            .IsEquivalentTo(expectedBeforeSequence, CollectionOrdering.Matching);
    }
}
