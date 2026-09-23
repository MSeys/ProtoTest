namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTestFact]
    [Tracking("MethodLevel", Order = 2)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterTestSupport.ExpectedBeforeSequence, "TestExecution"];
        Assert.Equal(expectedBeforeSequence, Proto.Context.Resolve<ExecutionLogState>().Log);
    }
}
