namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        AdapterTestSupport.ConfigureHost(builder);
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

public class TestService : ITestService
{
    public string GetMessage() => "ProtoTest_Xunit_Success";
}
