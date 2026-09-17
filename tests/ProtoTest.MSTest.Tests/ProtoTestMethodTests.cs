namespace ProtoTest.MSTest.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

[TestClass]
public class ProtoTestMethodTests
{
    [ProtoTest]
    public void ProtoTestMethod_ShouldResolveRegisteredService()
    {
        // Act
        var service = Proto.Context.Service<ITestService>();

        // Assert
        Assert.IsNotNull(service);
        Assert.AreEqual("MSTest_Integration_Success", service.GetMessage());
    }

    [ProtoTest]
    public async Task ProtoTestMethod_ShouldMaintainContext_AcrossAsyncAwaits()
    {
        // Arrange
        var contextState = new CustomState("MSTestUser");
        Proto.Context.SetContext(contextState);

        // Act
        await Task.Delay(10);
        var retrieved = Proto.Context.Resolve<CustomState>();

        // Assert
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("MSTestUser", retrieved.Name);
    }

    private sealed record CustomState(string Name) : IProtoContext;
}