namespace ProtoTest.Core.Tests;

using NUnit.Framework;

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
}
