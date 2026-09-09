namespace ProtoTest.Xunit;

using ProtoTest.Core;

/// <summary>
/// xUnit v2 has no native attachment protocol, so materialized artifact paths are written to test output.
/// </summary>
internal sealed class Xunit2AttachmentPublisher : IProtoTestAttachmentPublisher
{
    public static Xunit2AttachmentPublisher Instance { get; } = new();

    public async ValueTask PublishAsync(
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken = default)
    {
        var path = await attachment.MaterializeFileAsync(cancellationToken);
        Console.WriteLine($"ProtoTest attachment '{attachment.Name}': {path}");
    }
}
