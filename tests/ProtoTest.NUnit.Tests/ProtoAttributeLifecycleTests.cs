namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

[TestFixture]
[Tracking("ClassLevel", Order = 1)]
public class ProtoAttributeLifecycleTests
{

    [Test]
    [ProtoTest]
    [Tracking("MethodLevel", Order = 2)]
    public void ProtoTest_ShouldExecuteClassAndMethodAttributesInOrder()
    {
        // Act
        Proto.Context.Context<ExecutionLogState>().Log.Add("TestExecution");

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

        Assert.That(Proto.Context.Context<ExecutionLogState>().Log, Is.EqualTo(expectedBeforeSequence));
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class TrackingAttribute(string name) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        Proto.Context.Context<ExecutionLogState>().Log.Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        Proto.Context.Context<ExecutionLogState>().Log.Add($"{name}:After");
        return Task.CompletedTask;
    }
}