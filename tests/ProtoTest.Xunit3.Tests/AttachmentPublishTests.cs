namespace ProtoTest.Xunit3.Tests;

using ProtoTest.AdapterContract;
using ProtoTest.Core;
using Xunit;

public class AttachmentPublishTests
{
    [ProtoTestFact]
    public async Task Publisher_ShouldRegisterTheAttachmentWithXunit()
    {
        var attachment = AdapterProbes.CreateAttachment();
        var path = await attachment.MaterializeFileAsync();

        await Xunit3AttachmentPublisher.Instance.PublishAsync(attachment);

        Assert.True(File.Exists(path), $"the materialized attachment '{path}' must exist");
        Assert.Contains(AdapterProbes.AttachmentName, TestContext.Current!.Attachments!.Keys);
    }
}
