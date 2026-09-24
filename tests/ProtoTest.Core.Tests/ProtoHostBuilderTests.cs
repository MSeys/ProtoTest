namespace ProtoTest.Core.Tests;

/// <summary>
/// Stage 2 (Audit 3, finding B8): after the host is built, adding a resource or configuring tracing
/// would mutate the live host instead of composing it, so both are rejected like a second build.
/// </summary>
[TestFixture]
public sealed class ProtoHostBuilderTests
{
    [Test]
    public async Task AddResource_AfterBuild_ShouldThrow()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();
        var resource = new ProtoResource(
            "late",
            "probe",
            "Late resource",
            _ => ValueTask.CompletedTask,
            ProtoResourceScope.Run);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => builder.AddResource(resource));
        Assert.That(exception!.Message, Does.Contain("already built"));
    }

    [Test]
    public async Task ConfigureTracing_AfterBuild_ShouldThrow()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.ConfigureTracing(options => options.Enabled = false));
        Assert.That(exception!.Message, Does.Contain("already built"));
    }

    [Test]
    public async Task Build_Twice_ShouldThrow()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }
}
