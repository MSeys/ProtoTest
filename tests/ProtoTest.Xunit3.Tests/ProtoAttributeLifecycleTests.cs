namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using Xunit;

[Tracking("Class", Order = 2)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTestFact]
    [Tracking("Method", Order = 3)]
    public void ProtoTestFact_ShouldExecuteHooksAndAttributesInOrder()
    {
        // Act
        Proto.Context.Resolve<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        Assert.Equal(expectedBeforeSequence, Proto.Context.Resolve<ExecutionLogState>().Log);
    }
}
