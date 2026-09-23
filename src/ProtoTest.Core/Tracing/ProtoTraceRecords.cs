namespace ProtoTest.Core;

/// <summary>
/// Builds the observation, attachment and finding records the trace writers store, so their validation
/// and shape is one implementation. An id of <see cref="string.Empty"/> marks an orphan record the
/// recorder later attaches to nothing.
/// </summary>
internal static class ProtoTraceRecords
{
    public static void ValidateObservation(string targetName, string kind, string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
    }

    public static void ValidateAttachment(string name, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
    }

    public static void ValidateFinding(string message, string status, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
    }

    public static ProtoTraceObservationRecord Observation(
        string id,
        string? parentId,
        string targetName,
        string kind,
        string identifier,
        string? data = null,
        string? metadata = null)
    {
        ValidateObservation(targetName, kind, identifier);
        return new ProtoTraceObservationRecord(id, parentId, DateTimeOffset.UtcNow, targetName, kind, identifier, data, metadata);
    }

    public static ProtoTraceAttachmentRecord Attachment(
        string id,
        string? parentId,
        string name,
        string mediaType,
        string? description = null)
    {
        ValidateAttachment(name, mediaType);
        return new ProtoTraceAttachmentRecord(id, parentId, DateTimeOffset.UtcNow, name, mediaType, description);
    }

    public static ProtoTraceFindingRecord Finding(
        string id,
        string? parentId,
        string message,
        string status,
        string category,
        string? targetName = null,
        IReadOnlyList<string>? tags = null,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ValidateFinding(message, status, category);
        return new ProtoTraceFindingRecord(id, parentId, DateTimeOffset.UtcNow, message, status, category, targetName, tags, metadata);
    }
}
