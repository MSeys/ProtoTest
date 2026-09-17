namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

public class ProtoTestFactTests
{
    [ProtoTestFact]
    public void ProtoTestFact_ShouldResolveRegisteredService()
    {
        // Act
        var service = Proto.Context.Service<ITestService>();

        // Assert
        Assert.NotNull(service);
        Assert.Equal("Xunit3_Integration_Success", service.GetMessage());
    }

    [ProtoTestFact]
    public async Task ProtoTestFact_ShouldMaintainContext_AcrossAsyncAwaits()
    {
        // Arrange
        var contextState = new CustomState("V3User");
        Proto.Context.SetContext(contextState);

        // Act
        await Task.Delay(10);
        var retrieved = Proto.Context.Resolve<CustomState>();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("V3User", retrieved.Name);
    }

    private sealed record CustomState(string Name) : IProtoContext;
}