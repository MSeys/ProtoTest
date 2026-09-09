namespace ProtoTest.NUnit;

using global::NUnit.Framework;
using ProtoTest.Core;

internal sealed class NUnitAttachmentPublisher : IProtoTestAttachmentPublisher
{
    public static NUnitAttachmentPublisher Instance { get; } = new();

    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var path = await attachment.MaterializeFileAsync(cancellationToken);
        TestContext.AddTestAttachment(path, attachment.Description ?? attachment.Name);
    }
}
