namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
public class ProtoAttributeContextTests
{
    [Fact]
    [ProtoTest]
    [SetContextUser("Xunit2User")]
    public void ProtoTest_ShouldAccessContextSetByProtoAttribute()
    {
        // Act
        var userState = Proto.Context.Context<UserState>();

        // Assert
        Assert.NotNull(userState);
        Assert.Equal("Xunit2User", userState.Username);
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