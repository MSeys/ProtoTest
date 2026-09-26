namespace ProtoTest.Xunit.Tests;

using global::Xunit;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

[Collection(ProtoTestCollection.Name)]
public sealed class AttachmentPublishTests
{
    [Fact]
    public async Task Publisher_ShouldWriteTheMaterializedPathToTestOutput()
    {
        var attachment = AdapterProbes.CreateAttachment();
        var path = await attachment.MaterializeFileAsync();

        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await Xunit2AttachmentPublisher.Instance.PublishAsync(attachment);
        }
        finally
        {
            Console.SetOut(original);
        }

        Assert.Contains("adapter-attachment", writer.ToString());
        Assert.Contains(path, writer.ToString());
    }
}
