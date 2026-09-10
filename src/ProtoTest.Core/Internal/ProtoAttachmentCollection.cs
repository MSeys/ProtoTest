namespace ProtoTest.Core;

internal sealed class ProtoAttachmentCollection(string testId)
{
    private readonly ProtoLock _gate = new();
    private readonly List<ProtoTestAttachment> _attachments = [];
    private readonly string _namePrefix = $"{testId}-";

    public IReadOnlyList<ProtoTestAttachment> Snapshot()
    {
        lock (_gate)
        {
            return [.. _attachments];
        }
    }

    public ProtoTestAttachment Add(ProtoTestAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (!attachment.Name.StartsWith(_namePrefix, StringComparison.OrdinalIgnoreCase))
        {
            attachment = attachment.WithName($"{_namePrefix}{attachment.Name}");
        }

        lock (_gate)
        {
            if (_attachments.Any(existing =>
                    string.Equals(existing.Name, attachment.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"An attachment named '{attachment.Name}' is already registered for this test.");
            }

            _attachments.Add(attachment);
        }

        return attachment;
    }
}
