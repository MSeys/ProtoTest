namespace ProtoTest.MSTest.Tests;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;

[TestClass]
public class Setup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static void AssemblyInit(TestContext context)
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

    [AssemblyCleanup]
    public static async Task AssemblyCleanupAsync()
    {
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