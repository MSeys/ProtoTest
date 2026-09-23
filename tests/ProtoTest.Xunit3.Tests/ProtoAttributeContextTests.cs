namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
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
