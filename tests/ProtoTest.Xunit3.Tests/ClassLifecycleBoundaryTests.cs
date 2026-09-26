namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

/// <summary>
/// Characterization for the xUnit v3 class lifecycle. The scope starts in
/// the before-attribute, which xUnit v3 runs after class construction and <c>IAsyncLifetime.InitializeAsync</c>
/// and before <c>DisposeAsync</c>; these tests pin that boundary.
/// </summary>
public sealed class ClassLifecycleBoundaryTests : IAsyncLifetime
{
    private readonly bool _constructorSawContext;
    private bool _initializeSawContext;

    public ClassLifecycleBoundaryTests() => _constructorSawContext = TryGetContext(out _);

    public ValueTask InitializeAsync()
    {
        _initializeSawContext = TryGetContext(out _);
        return ValueTask.CompletedTask;
    }

    [ProtoTestFact]
    public void ClassCreation_ShouldCharacterizeAsOutsideTheLifecycle()
    {
        Assert.False(_constructorSawContext, "the constructor runs before the lifecycle starts");
        Assert.False(_initializeSawContext, "IAsyncLifetime.InitializeAsync runs before the lifecycle starts");
        Assert.NotNull(Proto.Context);
    }

    public ValueTask DisposeAsync()
    {
        // Characterization: class disposal runs after the after-attribute completed the lifecycle.
        Assert.False(TryGetContext(out _), "class disposal runs after the lifecycle completed");
        return ValueTask.CompletedTask;
    }

    private static bool TryGetContext(out ProtoExecutionContext? context)
    {
        try
        {
            context = Proto.Context;
            return true;
        }
        catch (InvalidOperationException)
        {
            context = null;
            return false;
        }
    }
}
