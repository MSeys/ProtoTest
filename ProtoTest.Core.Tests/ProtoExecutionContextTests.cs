namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

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
        var context = new ProtoExecutionContext("TestMethod", _scope, "test-id-1");
        var customState = new SampleContext("InitialData");

        // Act
        context.Set(customState);
        var retrieved = context.Get<SampleContext>();

        // Assert
        Assert.That(retrieved, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldReturnInstance_WhenContextExists()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "test-id-1");
        var customState = new SampleContext("Active");
        context.Set(customState);

        // Act
        var result = context.GetRequired<SampleContext>();

        // Assert
        Assert.That(result, Is.SameAs(customState));
    }

    [Test]
    public void GetRequired_ShouldThrowInvalidOperationException_WhenContextIsMissing()
    {
        // Arrange
        var context = new ProtoExecutionContext("TestMethod", _scope, "test-id-1");

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => context.GetRequired<SampleContext>());
        Assert.That(exception!.Message, Does.Contain(nameof(SampleContext)));
    }

    private sealed record SampleContext(string Value) : IProtoContext;
}