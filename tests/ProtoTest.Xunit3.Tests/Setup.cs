using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.Xunit3.Tests;

[assembly: AssemblyFixture(typeof(Setup))]

namespace ProtoTest.Xunit3.Tests;

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        AdapterTestSupport.ConfigureHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService>(_ => new ProbeTestService("Xunit3_Integration_Success"));
        });
    }
}

