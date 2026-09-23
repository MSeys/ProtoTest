namespace ProtoTest.NUnit.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestFixture]
public class ProtoAttributeContextTests
{
    [ProtoTest]
    public void ProtoTest_ShouldResolveRegisteredService()
    {
        var service = Proto.Context.Service<ITestService>();

        Assert.That(service.GetMessage(), Is.EqualTo("ProtoTest_NUnit_Success"));
    }

    [ProtoTest]
    [SetContextUser("NUnitUser")]
    public void ProtoTest_ShouldAccessContextSetByProtoAttribute()
    {
        var userState = Proto.Context.Resolve<UserState>();

        Assert.Multiple(() =>
        {
            Assert.That(userState, Is.Not.Null);
            Assert.That(userState.Username, Is.EqualTo("NUnitUser"));
        });
    }
}
