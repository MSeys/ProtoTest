namespace ProtoTest.NUnit.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[SetUpFixture]
public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        AdapterTestSupport.ConfigureHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService>(_ => new ProbeTestService("ProtoTest_NUnit_Success"));
        });
    }
}

