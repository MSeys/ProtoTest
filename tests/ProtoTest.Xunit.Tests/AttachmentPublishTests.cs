namespace ProtoTest.Xunit.Tests;

using ProtoTest.Core;
using global::Xunit;

[Collection(ProtoTestCollection.Name)]
public sealed class AttachmentPublishTests
{
    [Fact]
    public async Task Publisher_ShouldWriteTheMaterializedPathToTestOutput()
    {
        var attachment = ProtoTestAttachment.FromText(
            "adapter-attachment",
            "payload",
            description: "Shared adapter artifact");
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
