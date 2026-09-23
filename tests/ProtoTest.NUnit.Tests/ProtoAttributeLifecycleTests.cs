namespace ProtoTest.NUnit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestFixture]
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTest]
    [Tracking("MethodLevel", Order = 2)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        // Note: The AfterTest hooks execute after this test body completes,
        // so we check the Before execution log here within the test body.
        string[] expectedBeforeSequence = [.. AdapterTestSupport.ExpectedBeforeSequence, "TestExecution"];
        Assert.That(Proto.Context.Resolve<ExecutionLogState>().Log, Is.EqualTo(expectedBeforeSequence));
    }
}
