namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestClass]
[Tracking("Class", Order = 2)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTest]
    [Tracking("Method", Order = 3)]
    public void ProtoTestMethod_ShouldExecuteHooksAndAttributesInOrder()
    {
        var logState = Proto.Context.Resolve<ExecutionLogState>();

        // Act
        logState.Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterLifecycle.ExpectedBeforeSequence, "TestExecution"];
        CollectionAssert.AreEqual(expectedBeforeSequence, logState.Log);
    }
}
