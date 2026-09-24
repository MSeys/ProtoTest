namespace ProtoTest.Sheets.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
[Category("Characterization")]
public sealed class RegistrationIdempotencyTests
{
    [Test]
    public async Task AddSheets_CalledTwice_ShouldRegisterOneOptionsAndCollector()
    {
        var builder = new ProtoHostBuilder();
        IServiceCollection? services = null;
        builder.ConfigureServices(collection => services = collection);
        builder.AddSheets();
        builder.AddSheets();

        await using var host = builder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(SheetsOptions)), Is.EqualTo(1));
            Assert.That(services!.Count(descriptor => descriptor.ServiceType == typeof(IProtoCollector)), Is.EqualTo(1));
            Assert.That(
                services!.Count(descriptor => descriptor.ServiceType == typeof(ProtoCapabilityDescriptor)),
                Is.EqualTo(1));
        });

        await host.StartAsync();
        var context = await host.StartTestAsync("sheets idempotent", TestMethods.Placeholder);
        Assert.That(context.Service<SheetsOptions>(), Is.Not.Null);
        _ = context.Sheets();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }

    [Test]
    public async Task AddSheets_CalledTwice_ShouldApplyEveryConfigureCallback()
    {
        var builder = new ProtoHostBuilder();
        builder.AddSheets(options => options.IncludeHiddenSheets = false);
        builder.AddSheets(options => options.IncludeHiddenSheets = true);

        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("sheets compose", TestMethods.Placeholder);

        // Both callbacks run in registration order; the old first-wins behavior would leave this false.
        Assert.That(context.Service<SheetsOptions>().IncludeHiddenSheets, Is.True);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
    }
}
