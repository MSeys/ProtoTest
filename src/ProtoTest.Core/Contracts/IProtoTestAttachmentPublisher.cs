namespace ProtoTest.Core;

/// <summary>
/// Publishes Core attachments to a test framework or another artifact destination.
/// </summary>
public interface IProtoTestAttachmentPublisher
{
    ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default);
}
