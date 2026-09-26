namespace ProtoTest.NUnit.Tests;

using global::NUnit.Framework.Internal;
using ProtoTest.AdapterContract;

[TestFixture]
public sealed class AttachmentPublishTests
{
    [Test]
    public async Task Publisher_ShouldAttachTheFileToTheNUnitResult()
    {
        // The runner-side half of the publisher contract: NUnit's own result must receive the
        // attachment. Asserting only that the file exists would stay green for a publisher no-op.
        var attachment = AdapterProbes.CreateAttachment();
        var path = await attachment.MaterializeFileAsync();

        await NUnitAttachmentPublisher.Instance.PublishAsync(attachment);

        var attached = TestExecutionContext.CurrentContext.CurrentResult.TestAttachments
            .SingleOrDefault(candidate => candidate.FilePath == path);
        Assert.That(attached, Is.Not.Null, "the published attachment must reach the NUnit result");
        Assert.Multiple(() =>
        {
            Assert.That(attached!.Description, Is.EqualTo(AdapterProbes.AttachmentDescription));
            Assert.That(File.Exists(path), Is.True, "the published attachment must exist on disk");
        });
    }
}
