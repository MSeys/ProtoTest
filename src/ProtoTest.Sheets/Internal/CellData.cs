namespace ProtoTest.Sheets.Internal;

/// <summary>One raw cell as read from the worksheet, before it becomes a test-facing cell.</summary>
internal sealed record CellData(
    string Reference,
    string? Text,
    double? Number,
    bool? Boolean,
    DateTime? Date,
    string? Formula);
