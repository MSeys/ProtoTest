namespace ProtoTest.Core;

/// <summary>
/// A named binary payload a test produced - a downloaded report, an exported document. Integrations
/// that consume files accept this instead of a response type, so REST, GraphQL or an attachment can
/// feed them without any of them knowing about each other.
/// </summary>
public interface IProtoBinaryContent
{
    /// <summary>The media type, when the producer knows it.</summary>
    string? MediaType { get; }

    /// <summary>The bytes themselves.</summary>
    ReadOnlyMemory<byte> Content { get; }

    /// <summary>The file name, when the producer knows it (for example a Content-Disposition name).</summary>
    string? FileName { get; }
}
