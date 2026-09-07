namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public class ProtoExecutionContextDisposalTests
{
    [Test]
    public async Task DisposeAsync_ShouldDisposeClientsInReverseRegistrationOrder()
    {
        // Arrange
        using var rootProvider = new ServiceCollection().BuildServiceProvider();
        var scope = rootProvider.CreateScope();
        var disposalOrder = new List<string>();
        var context = new ProtoExecutionContext(
            "Test",
            scope,
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        context.RegisterClient(new TrackingClient("first", disposalOrder), "First");
        context.RegisterClient(new TrackingClient("second", disposalOrder), "Second");

        // Act
        await context.DisposeAsync();

        // Assert
        Assert.That(disposalOrder, Is.EqualTo(new[] { "second", "first" }));
    }

    private sealed class TrackingClient(string name, List<string> disposalOrder) : IDisposable
    {
        public void Dispose() => disposalOrder.Add(name);
    }
}