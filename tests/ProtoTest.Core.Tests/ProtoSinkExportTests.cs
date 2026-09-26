namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The export hook belongs to the host build, not to <c>AddSink</c> alone, so a sink
/// registered directly through DI exports too - and exactly once when both paths are used.
/// </summary>
[TestFixture]
public sealed class ProtoSinkExportTests
{
    [Test]
    public async Task SinkRegisteredDirectly_ShouldExportAtRunEnd()
    {
        // Arrange: no AddSink call at all - the DI registration is the only declaration.
        var sink = new CountingSink();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoSink>(sink));
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert
        Assert.That(sink.ExportCount, Is.EqualTo(1), "a directly registered sink is exported at run end");
    }

    [Test]
    public async Task SinkRegisteredDirectlyAndThroughAddSink_ShouldExportOnce()
    {
        // Arrange
        var sink = new CountingSink();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services => services.AddSingleton<IProtoSink>(sink));
        builder.AddSink<CountingSink>();
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: the build-time hook and AddSink's hook are one registration.
        Assert.That(sink.ExportCount, Is.EqualTo(1), "one sink declares one export, however it was registered");
    }
}
