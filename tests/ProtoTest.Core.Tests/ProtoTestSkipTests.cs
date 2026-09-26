namespace ProtoTest.Core.Tests;


[TestFixture]
public sealed class ProtoTestSkipTests
{
    [Test]
    public async Task SkipConditions_ShouldReportOnlyMissingCapabilities()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapability(new ProtoCapabilityDescriptor("Data", ProtoCapabilityKinds.Data, "tests"));
        await using var host = builder.Build();

        var present = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Data);
        var missing = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Server);
        var inProcess = new RequiresInProcessAttribute();

        Assert.Multiple(() =>
        {
            Assert.That(present.GetSkipReason(host), Is.Null);
            Assert.That(missing.GetSkipReason(host), Does.Contain(ProtoCapabilityKinds.Server));
            Assert.That(inProcess.GetSkipReason(host), Does.Contain("published"));
            Assert.That(
                ProtoTestSkip.GetReason([present, missing, inProcess], host),
                Does.Contain(ProtoCapabilityKinds.Server));
        });
    }

    [Test]
    public async Task SkipConditions_ShouldUseTheConfiguredReason()
    {
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();

        var attribute = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Store)
        {
            Reason = "no store today"
        };

        Assert.That(attribute.GetSkipReason(host), Is.EqualTo("no store today"));
    }

    [Test]
    public async Task RequiresWorker_ShouldCheckTheProgramAssemblyCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapability(new ProtoCapabilityDescriptor(
            typeof(SampleWorker).Assembly.GetName().Name!,
            ProtoCapabilityKinds.Worker,
            "tests"));
        await using var host = builder.Build();

        var satisfied = new RequiresWorkerAttribute<SampleWorker>();
        var missing = new RequiresWorkerAttribute<ProtoHost>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(satisfied.GetSkipReason(host), Is.Null);
            Assert.That(missing.GetSkipReason(host), Does.Contain("AddWorkerHost<ProtoHost>"));
        }
    }

    [Test]
    public async Task RequiresServer_ShouldCheckTheNamedInstance()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapability(new ProtoCapabilityDescriptor("ASP.NET Core", ProtoCapabilityKinds.Server, "tests")
        {
            Instance = "Api"
        });
        await using var host = builder.Build();

        var satisfied = new RequiresServerAttribute("Api");
        var missing = new RequiresServerAttribute("Billing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(satisfied.GetSkipReason(host), Is.Null);
            Assert.That(missing.GetSkipReason(host), Does.Contain("Billing"));
            Assert.That(missing.GetSkipReason(host), Does.Contain("AddAspNetCoreServer"));
        }
    }

    [Test]
    public async Task RequiresServer_ShouldNotMatchADescriptorNameWithoutTheInstance()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapability(new ProtoCapabilityDescriptor("Api", ProtoCapabilityKinds.Server, "tests"));
        await using var host = builder.Build();

        // The descriptor name is not the instance: a server registered under "Api" as its capability
        // name is not the AddAspNetCoreServer instance named "Api".
        Assert.That(new RequiresServerAttribute("Api").GetSkipReason(host), Is.Not.Null);
    }

    [Test]
    public async Task RequiresApplication_ShouldCheckTheDeclaredApplication()
    {
        var builder = new ProtoHostBuilder();
        builder.AddApplication("Api", _ => { });
        await using var host = builder.Build();

        var satisfied = new RequiresApplicationAttribute("Api");
        var missing = new RequiresApplicationAttribute("Billing");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(satisfied.GetSkipReason(host), Is.Null);
            Assert.That(missing.GetSkipReason(host), Does.Contain("Billing"));
            Assert.That(missing.GetSkipReason(host), Does.Contain("AddApplication"));
        }
    }

    [Test]
    public async Task SuiteReason_ShouldBeUsedWhenTheGateHasNoReason()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapabilityReason(ProtoCapabilityKinds.Broker, "start the broker");
        await using var host = builder.Build();

        var attribute = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Broker);

        Assert.That(attribute.GetSkipReason(host), Is.EqualTo("start the broker"));
    }

    [Test]
    public async Task SuiteReason_ShouldLoseToTheAttributesOwnReason()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapabilityReason(ProtoCapabilityKinds.Broker, "start the broker");
        await using var host = builder.Build();

        var attribute = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Broker)
        {
            Reason = "this test needs a broker"
        };

        Assert.That(attribute.GetSkipReason(host), Is.EqualTo("this test needs a broker"));
    }

    [Test]
    public async Task SuiteReason_ShouldFallBackToTheDefaultForAnUnknownKind()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapabilityReason(ProtoCapabilityKinds.Broker, "start the broker");
        await using var host = builder.Build();

        var attribute = new RequiresCapabilityAttribute(ProtoCapabilityKinds.Store);

        Assert.That(attribute.GetSkipReason(host), Does.Contain(ProtoCapabilityKinds.Store));
    }

    [Test]
    public async Task SuiteReason_ShouldNarrowToTheNamedInstance()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapabilityReason(ProtoCapabilityKinds.Server, "start the console", "Console");
        await using var host = builder.Build();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new RequiresServerAttribute("Console").GetSkipReason(host), Is.EqualTo("start the console"));
            Assert.That(new RequiresServerAttribute("Api").GetSkipReason(host), Does.Contain("Api"));
        }
    }

    [Test]
    public async Task SuiteReason_ShouldFallBackToTheKindWhenNoNameMatches()
    {
        var builder = new ProtoHostBuilder();
        builder.AddCapabilityReason(ProtoCapabilityKinds.Server, "start the server");
        await using var host = builder.Build();

        Assert.That(new RequiresServerAttribute("Api").GetSkipReason(host), Is.EqualTo("start the server"));
    }

    private sealed class SampleWorker
    {
    }
}
