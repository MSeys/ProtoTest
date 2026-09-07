using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Xunit3.Tests;

[assembly: AssemblyFixture(typeof(Setup))]

namespace ProtoTest.Xunit3.Tests;

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddTestHook<TrackingHook>();
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService, TestService>();
        });
    }
}

public interface ITestService
{
    string GetMessage();
}

public class TestService : ITestService
{
    public string GetMessage() => "Xunit3_Integration_Success";
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