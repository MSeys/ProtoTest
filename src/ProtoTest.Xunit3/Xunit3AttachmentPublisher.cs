namespace ProtoTest.Xunit3;

using ProtoTest.Core;

internal sealed class Xunit3AttachmentPublisher : IProtoTestAttachmentPublisher
{
    public static Xunit3AttachmentPublisher Instance { get; } = new();

    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var content = await attachment.ReadAllBytesAsync(cancellationToken);
        Xunit.TestContext.Current.AddAttachment(
            attachment.Name,
            content,
            replaceExistingValue: false,
            attachment.MediaType);
    }
}
