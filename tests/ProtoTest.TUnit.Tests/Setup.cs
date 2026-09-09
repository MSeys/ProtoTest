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
            builder.AddTestHook<TrackingHook>();
            builder.AddTestHook<AdapterContractHook>();
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

public interface ITestService
{
    string GetMessage();
}

public class TestService : ITestService
{
    public string GetMessage() => "TUnit_Integration_Success";
}

public class ExecutionLogState : IProtoContext
{
    public List<string> Log { get; } = [];
}

public class TrackingHook : IProtoTestHook
{
    public int Order => 1;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        GetOrCreateLog(context).Add("Hook:Before");
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    public static List<string> GetOrCreateLog(ProtoExecutionContext context)
    {
        var state = context.TryContext<ExecutionLogState>();
        if (state == null)
        {
            state = new ExecutionLogState();
            context.SetContext(state);
        }
        return state.Log;
    }
}
