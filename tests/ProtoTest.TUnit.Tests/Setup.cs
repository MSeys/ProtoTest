using ProtoTest.TUnit;
using TUnit.Core.Executors;

[assembly: TestExecutor<ProtoTestExecutor>()]

namespace ProtoTest.TUnit.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

public class Setup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static async Task AssemblyInitAsync(AssemblyHookContext _)
    {
        await InitializeAsync(builder =>
        {
            AdapterTestSupport.ConfigureHost(builder);
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ITestService, TestService>();
            });
        });
    }

    [After(Assembly)]
    public static async Task AssemblyCleanupAsync(AssemblyHookContext _)
    {
        await CleanupAsync();
    }
}

public class TestService : ITestService
{
    public string GetMessage() => "TUnit_Integration_Success";
}
