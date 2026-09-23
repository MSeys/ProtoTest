namespace ProtoTest.MSTest.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[TestClass]
public class Setup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static async Task AssemblyInitAsync(TestContext context)
    {
        await InitializeAsync(builder =>
        {
            AdapterTestSupport.ConfigureHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ITestService>(_ => new ProbeTestService("MSTest_Integration_Success"));
            });
        });
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanupAsync()
    {
        await CleanupAsync();
    }
}

