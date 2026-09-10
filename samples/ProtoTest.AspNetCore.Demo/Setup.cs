namespace ProtoTest.AspNetCore.Demo;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.SampleApi;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[SetUpFixture]
public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.json", optional: false)
                      .AddJsonFile("appsettings.Development.json", optional: true);
            })
            .AddRest()
            .AddAspNetCoreServer<Program>("OrderApi", webHost =>
                webHost.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITestMessageService>();
                    services.AddSingleton<ITestMessageService, DemoMessageService>();
                }));
    }

    private sealed class DemoMessageService : ITestMessageService
    {
        public string GetMessage() => "Hello from the test service override!";
    }
}
