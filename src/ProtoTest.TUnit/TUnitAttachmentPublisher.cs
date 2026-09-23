namespace ProtoTest.TUnit;

using ProtoTest.Core;

internal sealed class TUnitAttachmentPublisher(TestContext context) : ProtoTestAttachmentPublisher
{
    protected override ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        context.Output.AttachArtifact(path, attachment.Name, attachment.Description ?? string.Empty);
        return ValueTask.CompletedTask;
    }
}
