namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

[TestFixture]
public class ProtoAttributeContextTests
{
    [ProtoTest]
    [SetContextUser("NUnitUser")]
    public void ProtoTest_ShouldAccessContextSetByProtoAttribute()
    {
        // Act
        var userState = Proto.Context<UserState>();

        // Assert
        Assert.That(userState, Is.Not.Null);
        Assert.That(userState.Username, Is.EqualTo("NUnitUser"));
    }
}

// Custom ProtoAttribute die context instelt vóór de test draait
[AttributeUsage(AttributeTargets.Method)]
public class SetContextUserAttribute(string username) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        Proto.SetContext(new UserState(username));
        return Task.CompletedTask;
    }
}

public sealed record UserState(string Username) : IProtoContext;