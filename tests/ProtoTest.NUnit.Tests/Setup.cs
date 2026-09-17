namespace ProtoTest.NUnit.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[SetUpFixture]
public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddTestHook<TrackingHook>();
        builder.AddTestHook<AdapterContractHook>();
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService, TestService>();
        });
    }
}

public interface ITestService
{
    string GetValue();
}

public class TestService : ITestService
{
    public string GetValue() => "ProtoTest_NUnit_Success";
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
        var state = context.TryResolve<ExecutionLogState>();
        if (state == null)
        {
            state = new ExecutionLogState();
            context.SetContext(state);
        }
        return state.Log;
    }
}
