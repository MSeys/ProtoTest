namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        var expectedBeforeSequence = new[]
        {
            "Hook:Before",
            "ClassLevel:Before",
            "MethodLevel:Before",
            "TestExecution"
        };

        CollectionAssert.AreEqual(expectedBeforeSequence, logState.Log);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TrackingAttribute(string name) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        TrackingHook.GetOrCreateLog(context).Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}