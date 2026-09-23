namespace ProtoTest.MSTest;

using ProtoTest.Core;

internal sealed class MSTestAttachmentPublisher : ProtoTestAttachmentPublisher
{
    private readonly List<string> _files = [];

    public IReadOnlyList<string> Files => _files;

    protected override ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        _files.Add(path);
        return ValueTask.CompletedTask;
    }
}
