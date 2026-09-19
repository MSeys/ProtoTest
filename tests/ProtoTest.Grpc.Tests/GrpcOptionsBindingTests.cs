namespace ProtoTest.Grpc.Tests;

using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;

[TestFixture]
public sealed class GrpcOptionsBindingTests
{
    [Test]
    public void SensitiveMetadataKeys_ShouldExtendTheDefaultsFromTheSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:Grpc:SensitiveMetadataKeys:0"] = "x-custom"
            })
            .Build();
        var options = new GrpcClientOptions();

        options.BindFromConfiguration(configuration);

        Assert.Multiple(() =>
        {
            Assert.That(options.SensitiveMetadataKeys, Does.Contain("authorization"));
            Assert.That(options.SensitiveMetadataKeys, Does.Contain("x-custom"));
        });
    }
}
