namespace ProtoTest.TUnit.Tests;

using ProtoTest.Core;

public class ProtoTUnitTestCases
{
    [Test]
    public async Task TUnit_ShouldResolveRegisteredService()
    {
        // Act
        var service = Proto.Service<ITestService>();

        // Assert
        await Assert.That(service).IsNotNull();
        await Assert.That(service.GetMessage()).IsEqualTo("TUnit_Integration_Success");
    }

    [Test]
    public async Task TUnit_ShouldMaintainContext_AcrossAsyncAwaits()
    {
        // Arrange
        var contextState = new CustomState("TUnitUser");
        Proto.SetContext(contextState);

        // Act
        await Task.Delay(10);
        var retrieved = Proto.Context<CustomState>();

        // Assert
        await Assert.That(retrieved).IsNotNull();
        await Assert.That(retrieved!.Name).IsEqualTo("TUnitUser");
    }

    private sealed record CustomState(string Name) : IProtoContext;
}