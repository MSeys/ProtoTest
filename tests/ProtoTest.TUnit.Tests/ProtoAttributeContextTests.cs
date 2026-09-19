namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

public class ProtoAttributeContextTests
{
    [Test]
    [SetContextUser("TUnitUser")]
    public async Task ProtoTest_ShouldAccessContextSetByProtoAttribute()
    {
        // Act
        var userState = Proto.Context.Resolve<UserState>();

        // Assert
        await Assert.That(userState).IsNotNull();
        await Assert.That(userState.Username).IsEqualTo("TUnitUser");
    }
}

// Custom ProtoAttribute that sets context before the test runs
[AttributeUsage(AttributeTargets.Method)]
public class SetContextUserAttribute(string username) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        Proto.Context.SetContext(new UserState(username));
        return Task.CompletedTask;
    }
}

public sealed record UserState(string Username) : IProtoContext;