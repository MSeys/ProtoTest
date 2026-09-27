using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;
using ProtoTest.Xunit3;
using ProtoTest.Xunit3.AutoWrap.Tests;

// The suite opts in: every plain [Fact] below runs inside a ProtoTest execution context.
[assembly: ProtoTestAutoWrap]
[assembly: AssemblyFixture(typeof(Setup))]

namespace ProtoTest.Xunit3.AutoWrap.Tests;

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        AdapterLifecycle.ConfigureHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService>(_ => new ProbeTestService("ProtoTest_Xunit3_AutoWrap_Success"));
        });
    }
}
