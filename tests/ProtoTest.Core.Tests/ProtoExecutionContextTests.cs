namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

[TestFixture]
public class ProtoExecutionContextTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;

    [SetUp]
    public void SetUp()
    {
        _serviceProvider = new ServiceCollection().BuildServiceProvider();
        _scope = _serviceProvider.CreateScope();
    }

    [TearDown]
    public void TearDown()
    {
        _scope.Dispose();
        _serviceProvider.Dispose();
    }

    [Test]
    public void SetAndGet_ShouldStoreAndRetrieveTypedContext()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        var customState = new SampleContext("InitialData");

        // Act
        context.SetContext(customState);
        var retrieved = context.TryContext<SampleContext>();

        // Assert
        Assert.That(retrieved, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldReturnInstance_WhenContextExists()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        var customState = new SampleContext("Active");
        context.SetContext(customState);

        // Act
        var result = context.Context<SampleContext>();

        // Assert
        Assert.That(result, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldThrowInvalidOperationException_WhenContextIsMissing()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => context.Context<SampleContext>());
        Assert.That(exception!.Message, Does.Contain(nameof(SampleContext)));
    }

    private sealed record SampleContext(string Value) : IProtoContext;
}
