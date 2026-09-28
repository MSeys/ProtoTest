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
        // The AfterTest hooks run after this body completes, so the Before log is asserted here.
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        Assert.That(Proto.Context.Resolve<ExecutionLogState>().Log, Is.EqualTo(expectedBeforeSequence));
    }
}
