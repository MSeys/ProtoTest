namespace ProtoTest.Grpc.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

[TestFixture]
public sealed class GrpcOptionsBindingTests
{
    [Test]
    public void SensitiveMetadataKeys_ShouldExtendTheDefaultsFromTheLegacySection()
    {
        var options = new GrpcClientOptions();

        options.BindFromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["ProtoTest:Grpc:SensitiveMetadataKeys:0"] = "x-custom"
        }));

        Assert.That(options.SensitiveMetadataKeys, Does.Contain("authorization"));
        Assert.That(options.SensitiveMetadataKeys, Does.Contain("x-custom"));
    }

    [Test]
    public void DefaultDeadline_ShouldBindFromTheLegacySection()
    {
        var options = new GrpcClientOptions();

        options.BindFromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["ProtoTest:Grpc:DefaultDeadline"] = "00:00:07"
        }));

        Assert.That(options.DefaultDeadline, Is.EqualTo(TimeSpan.FromSeconds(7)));
    }

    [Test]
    public void DefaultDeadline_ShouldBindFromTheClientSection()
    {
        var options = new GrpcClientOptions();

        options.BindFromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["ProtoTest:Grpc:Client:DefaultDeadline"] = "00:00:09"
        }));

        Assert.That(options.DefaultDeadline, Is.EqualTo(TimeSpan.FromSeconds(9)));
    }

    [Test]
    public void DefaultDeadline_ShouldPreferTheClientSectionOverTheLegacyOne()
    {
        var options = new GrpcClientOptions();

        options.BindFromConfiguration(Configuration(new Dictionary<string, string?>
        {
            ["ProtoTest:Grpc:DefaultDeadline"] = "00:00:07",
            ["ProtoTest:Grpc:Client:DefaultDeadline"] = "00:00:09"
        }));

        Assert.That(options.DefaultDeadline, Is.EqualTo(TimeSpan.FromSeconds(9)));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
