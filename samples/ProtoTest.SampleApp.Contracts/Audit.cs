namespace ProtoTest.SampleApp.Contracts;

public sealed record AuditMetadataEntry(string Key, string Value);

public sealed record AuditEventResponse(
    long Sequence,
    string Action,
    string Resource,
    string Actor,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<AuditMetadataEntry> Metadata);
