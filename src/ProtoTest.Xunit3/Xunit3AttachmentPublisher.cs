namespace ProtoTest.Xunit3;

using ProtoTest.Core;

internal sealed class Xunit3AttachmentPublisher : ProtoTestAttachmentPublisher
{
    public static Xunit3AttachmentPublisher Instance { get; } = new();

    protected override async ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        var content = await File.ReadAllBytesAsync(path, cancellationToken);
        Xunit.TestContext.Current.AddAttachment(
            attachment.Name,
            content,
            replaceExistingValue: false,
            attachment.MediaType);
    }
}
