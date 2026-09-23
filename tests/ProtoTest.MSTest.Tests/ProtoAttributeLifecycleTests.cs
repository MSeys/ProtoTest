namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestClass]
[Tracking("ClassLevel", Order = 2)]
public class ProtoAttributeLifecycleTests
{
    [ProtoTest]
    [Tracking("MethodLevel", Order = 3)]
    public void ProtoTestMethod_ShouldExecuteHooksAndAttributesInOrder()
    {
        // Fetch test-scoped log state
        var logState = Proto.Context.Resolve<ExecutionLogState>();

        // Act
        logState.Log.Add("TestExecution");

        // Assert
        string[] expectedBeforeSequence = [.. AdapterTestSupport.ExpectedBeforeSequence, "TestExecution"];
        CollectionAssert.AreEqual(expectedBeforeSequence, logState.Log);
    }
}
