namespace ProtoTest.NUnit.Tests;

using ProtoTest.Core;

[TestFixture]
public sealed class AttachmentPublishTests
{
    [Test]
    public async Task Publisher_ShouldMaterializeAndAttachTheFileToTheTest()
    {
        var attachment = ProtoTestAttachment.FromText(
            "adapter-attachment",
            "payload",
            description: "Shared adapter artifact");
        var path = await attachment.MaterializeFileAsync();

        await NUnitAttachmentPublisher.Instance.PublishAsync(attachment);

        Assert.That(File.Exists(path), Is.True, "the published attachment must exist on disk");
    }
}
