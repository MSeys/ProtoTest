namespace ProtoTest.Xunit;

using ProtoTest.Core;

/// <summary>
/// xUnit v2 has no native attachment protocol, so materialized artifact paths are written to test output.
/// </summary>
internal sealed class Xunit2AttachmentPublisher : ProtoTestAttachmentPublisher
{
    public static Xunit2AttachmentPublisher Instance { get; } = new();

    protected override ValueTask PublishFileAsync(
        string path,
        ProtoTestAttachment attachment,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"ProtoTest attachment '{attachment.Name}': {path}");
        return ValueTask.CompletedTask;
    }
}
