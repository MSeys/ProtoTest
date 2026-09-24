namespace ProtoTest.NUnit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestFixture]
[Tracking("Class", Order = 10)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTest]
    [Tracking("Method", Order = 20)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        // Note: The AfterTest hooks execute after this test body completes,
        // so we check the Before execution log here within the test body.
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        Assert.That(Proto.Context.Resolve<ExecutionLogState>().Log, Is.EqualTo(expectedBeforeSequence));
    }
}
