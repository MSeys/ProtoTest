namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

/// <summary>
/// Tests the lifecycle execution order of class-level and method-level <see cref="ProtoAttribute"/> instances in TUnit.
/// </summary>
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{
    [Test]
    [Tracking("MethodLevel", Order = 2)]
    public async Task ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context<ExecutionLogState>().Log.Add("TestExecution");

        // Assert
        // Note: The AfterTest hooks execute after this test body completes,
        // so we check the Before execution log here within the test body.
        var expectedBeforeSequence = new[]
        {
            "Hook:Before",
            "ClassLevel:Before",
            "MethodLevel:Before",
            "TestExecution"
        };

        await Assert.That(Proto.Context<ExecutionLogState>().Log)
            .IsEquivalentTo(expectedBeforeSequence);
    }
}

/// <summary>
/// Custom tracking attribute used to verify attribute lifecycle hook execution order.
/// </summary>
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