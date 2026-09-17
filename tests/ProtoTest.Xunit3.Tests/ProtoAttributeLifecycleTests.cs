namespace ProtoTest.Xunit3.Tests;

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
        var expectedBeforeSequence = new[]
        {
            "Hook:Before",
            "ClassLevel:Before",
            "MethodLevel:Before",
            "TestExecution"
        };


        Assert.Equal(expectedBeforeSequence, Proto.Context.Resolve<ExecutionLogState>().Log);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TrackingAttribute(string name) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        Proto.Context.Resolve<ExecutionLogState>().Log.Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        Proto.Context.Resolve<ExecutionLogState>().Log.Add($"{name}:After");
        return Task.CompletedTask;
    }
}