namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using ProtoTest.Core;

internal sealed class NUnitAttachmentPublisher : ProtoTestAttachmentPublisher
{
    public static NUnitAttachmentPublisher Instance { get; } = new();

    protected override ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        TestContext.AddTestAttachment(path, attachment.Description ?? attachment.Name);
        return ValueTask.CompletedTask;
    }
}
