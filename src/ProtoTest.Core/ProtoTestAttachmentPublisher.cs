namespace ProtoTest.Core;

/// <summary>
/// Materializes an attachment to a file and hands the path to the framework. Every adapter shares this
/// shape, so a publisher declares only what its framework does with the file.
/// </summary>
public abstract class ProtoTestAttachmentPublisher : IProtoTestAttachmentPublisher
{
    /// <inheritdoc />
    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        var path = await attachment.MaterializeFileAsync(cancellationToken);
        await PublishFileAsync(path, attachment, cancellationToken);
    }

    /// <summary>Publishes one materialized file to the framework.</summary>
    protected abstract ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken);
}
