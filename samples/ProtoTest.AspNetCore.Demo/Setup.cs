namespace ProtoTest.AspNetCore.Demo;

using Microsoft.Extensions.Configuration;
using ProtoTest.AspNetCore;
using ProtoTest.AspNetCore.SampleApi;
using ProtoTest.Core;
using ProtoTest.NUnit;

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
            .AddAspNetCoreServer<Program>("OrderApi");
    }
}