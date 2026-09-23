namespace ProtoTest.TUnit.Tests;

using global::TUnit.Core;
using ProtoTest.Core;

public class AttachmentPublishTests
{
    [Test]
    public async Task Publisher_ShouldMaterializeAndAttachTheFileToTheTest()
    {
        var attachment = ProtoTestAttachment.FromText(
            "adapter-attachment",
            "payload",
            description: "Shared adapter artifact");
        var path = await attachment.MaterializeFileAsync();

        var publisher = new TUnitAttachmentPublisher(TestContext.Current!);
        await publisher.PublishAsync(attachment);

        await Assert.That(File.Exists(path)).IsTrue();
    }
}
