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
            builder.AddTestHook<TrackingHook>();
            builder.AddTestHook<AdapterContractHook>();
            builder.ConfigureServices(services =>
            {
                services.AddScoped<ITestService, TestService>();
            });
        });
    }

    [AssemblyCleanup]
    public static async Task AssemblyCleanupAsync()
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
    public string GetMessage() => "MSTest_Integration_Success";
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
