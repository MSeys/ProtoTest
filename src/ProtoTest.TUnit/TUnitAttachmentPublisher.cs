namespace ProtoTest.TUnit;

using ProtoTest.Core;

internal sealed class TUnitAttachmentPublisher(TestContext context)
    : IProtoTestAttachmentPublisher
{
    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var path = await attachment.MaterializeFileAsync(cancellationToken);
        context.Output.AttachArtifact(path, attachment.Name, attachment.Description ?? string.Empty);
    }
}
