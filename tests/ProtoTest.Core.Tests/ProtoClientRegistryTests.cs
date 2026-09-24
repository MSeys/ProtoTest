namespace ProtoTest.Core.Tests;

using ProtoTest.Core.Internal;

/// <summary>
/// Stage 2 (Audit 3, finding B7): a client alias is a lookup, not an ownership registration, so it must
/// obey the same seal as a client registration once the context starts releasing resources.
/// </summary>
[TestFixture]
public sealed class ProtoClientRegistryTests
{
    [Test]
    public void RegisterAlias_AfterSeal_ShouldThrow()
    {
        // Arrange
        var registry = new ProtoClientRegistry();
        registry.Register(new object(), "Default");
        registry.Seal();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => registry.RegisterAlias(typeof(object), "alias", new object()));
    }
}
