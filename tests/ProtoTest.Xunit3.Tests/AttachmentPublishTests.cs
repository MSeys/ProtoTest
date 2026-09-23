namespace ProtoTest.Xunit3.Tests;

using ProtoTest.Core;
using Xunit;

public class AttachmentPublishTests
{
    [ProtoTestFact]
    public async Task Publisher_ShouldRegisterTheAttachmentWithXunit()
    {
        var attachment = ProtoTestAttachment.FromText(
            "adapter-attachment",
            "payload",
            description: "Shared adapter artifact");
        var path = await attachment.MaterializeFileAsync();

        await Xunit3AttachmentPublisher.Instance.PublishAsync(attachment);

        Assert.True(File.Exists(path), $"the materialized attachment '{path}' must exist");
        Assert.Contains("adapter-attachment", TestContext.Current!.Attachments!.Keys);
    }
}
