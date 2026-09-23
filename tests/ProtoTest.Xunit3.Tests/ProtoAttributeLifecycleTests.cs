namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using Xunit;

[Tracking("ClassLevel", Order = 2)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTestFact]
    [Tracking("MethodLevel", Order = 3)]
    public void ProtoTestFact_ShouldExecuteHooksAndAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterTestSupport.ExpectedBeforeSequence, "TestExecution"];
        Assert.Equal(expectedBeforeSequence, Proto.Context.Resolve<ExecutionLogState>().Log);
    }
}
