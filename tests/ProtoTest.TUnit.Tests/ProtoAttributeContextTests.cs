namespace ProtoTest.TUnit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;

public class ProtoAttributeContextTests
{
    [Test]
    public async Task ProtoTest_ShouldResolveRegisteredService()
    {
        var service = Proto.Context.Service<ITestService>();

        await Assert.That(service.GetMessage()).IsEqualTo("TUnit_Integration_Success");
    }

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
