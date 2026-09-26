namespace ProtoTest.TUnit.Tests;

using global::TUnit.Core;
using ProtoTest.AdapterContract;
using ProtoTest.Core;

public class AttachmentPublishTests
{
    [Test]
    public async Task Publisher_ShouldAttachTheFileToTheTUnitTest()
    {
        // The runner-side half of the publisher contract: TUnit's own test context must receive the
        // attachment. Asserting only that the file exists would stay green for a publisher no-op.
        var attachment = AdapterProbes.CreateAttachment();
        var path = await attachment.MaterializeFileAsync();

        var publisher = new TUnitAttachmentPublisher(TestContext.Current!);
        await publisher.PublishAsync(attachment);

        var attached = TestContext.Current!.Output.Artifacts
            .SingleOrDefault(artifact => artifact.File.FullName == path);
        await Assert.That(attached).IsNotNull();
        await Assert.That(attached!.DisplayName).IsEqualTo(AdapterProbes.AttachmentName);
        await Assert.That(File.Exists(path)).IsTrue();
    }
}
