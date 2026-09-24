namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
[Tracking("Class", Order = 10)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTestFact]
    [Tracking("Method", Order = 20)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        Assert.Equal(expectedBeforeSequence, Proto.Context.Resolve<ExecutionLogState>().Log);
    }
}
