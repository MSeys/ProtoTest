namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{
    [Fact]
    [ProtoTest]
    [Tracking("MethodLevel", Order = 2)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        var expectedBeforeSequence = new[]
        {
            "Hook:Before",
            "ClassLevel:Before",
            "MethodLevel:Before",
            "TestExecution"
        };

        Assert.Equal(expectedBeforeSequence, Proto.Context<ExecutionLogState>().Log);
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TrackingAttribute(string name) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        Proto.Context<ExecutionLogState>().Log.Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        Proto.Context<ExecutionLogState>().Log.Add($"{name}:After");
        return Task.CompletedTask;
    }
}