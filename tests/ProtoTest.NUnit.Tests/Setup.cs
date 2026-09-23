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
            services.AddScoped<ITestService, TestService>();
        });
    }
}

public class TestService : ITestService
{
    public string GetMessage() => "ProtoTest_NUnit_Success";
}
