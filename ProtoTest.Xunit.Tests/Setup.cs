namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddHook<TrackingHook>();
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService, TestService>();
        });
    }
}

[CollectionDefinition(Name)]
public class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}

public interface ITestService
{
    string GetValue();
}

public class TestService : ITestService
{
    public string GetValue() => "ProtoTest_Xunit_Success";
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