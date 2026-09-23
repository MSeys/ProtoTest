namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
public class ProtoAttributeContextTests
{
    [ProtoTestFact]
    [SetContextUser("Xunit2User")]
    public void ProtoTest_ShouldAccessContextSetByProtoAttribute()
    {
        // Act
        var userState = Proto.Context.Resolve<UserState>();

        // Assert
        Assert.NotNull(userState);
        Assert.Equal("Xunit2User", userState.Username);
    }
}
