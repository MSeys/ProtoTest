namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

public class ProtoAttributeContextTests
{
    [ProtoTestFact]
    [SetContextUser("Xunit3User")]
    public void ProtoTestFact_ShouldAccessContextSetByProtoAttribute()
    {
        // Act
        var userState = Proto.Context.Resolve<UserState>();

        // Assert
        Assert.NotNull(userState);
        Assert.Equal("Xunit3User", userState.Username);
    }
}

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