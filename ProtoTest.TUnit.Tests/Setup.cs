using ProtoTest.TUnit;
using TUnit.Core.Executors;

[assembly: TestExecutor<ProtoTestExecutor>()]

namespace ProtoTest.TUnit.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public class Setup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static void AssemblyInit(AssemblyHookContext _)
    {
        Initialize(builder =>
        {
            builder.AddHook<TrackingHook>();
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

public class TrackingHook : IProtoHook
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
        var state = context.Get<ExecutionLogState>();
        if (state == null)
        {
            state = new ExecutionLogState();
            context.Set(state);
        }
        return state.Log;
    }
}