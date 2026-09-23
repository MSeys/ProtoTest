namespace ProtoTest.Sheets.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
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


}
