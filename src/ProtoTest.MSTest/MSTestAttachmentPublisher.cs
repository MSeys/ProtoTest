namespace ProtoTest.MSTest;

using ProtoTest.Core;

internal sealed class MSTestAttachmentPublisher : IProtoTestAttachmentPublisher
{
    private readonly List<string> _files = [];

    public IReadOnlyList<string> Files => _files;

    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        _files.Add(await attachment.MaterializeFileAsync(cancellationToken));
    }
}
