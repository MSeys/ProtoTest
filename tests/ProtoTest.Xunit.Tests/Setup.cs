namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        AdapterLifecycle.ConfigureHost(builder);
        builder.ConfigureServices(services =>
        {
            services.AddScoped<ITestService>(_ => new ProbeTestService("ProtoTest_Xunit_Success"));
        });
    }
}

[CollectionDefinition(Name)]
public class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}

