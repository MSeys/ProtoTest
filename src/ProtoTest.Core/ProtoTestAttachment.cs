namespace ProtoTest.Core;

using System.Text;

/// <summary>
/// A file or in-memory artifact associated with one test execution.
/// </summary>
public sealed class ProtoTestAttachment
{
    private readonly byte[]? _content;
    private readonly object _materializationGate = new();
    private Task<string>? _materialization;

    private ProtoTestAttachment(
        string name,
        string mediaType,
        string? description,
        string? filePath,
        byte[]? content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        Name = name;
        MediaType = mediaType;
        Description = description;
        FilePath = filePath;
        _content = content;
    }

    public string Name { get; }
    public string MediaType { get; }
    public string? Description { get; }
    public string? FilePath { get; }
    public bool IsFile => FilePath is not null;

    internal ProtoTestAttachment WithName(string name)
        => new(name, MediaType, Description, FilePath, _content);

    public static ProtoTestAttachment FromText(
        string name,
        string content,
        string mediaType = "text/plain",
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new ProtoTestAttachment(name, mediaType, description, null, Encoding.UTF8.GetBytes(content));
    }

    public static ProtoTestAttachment FromBytes(
        string name,
        ReadOnlyMemory<byte> content,
        string mediaType = "application/octet-stream",
        string? description = null)
        => new(name, mediaType, description, null, content.ToArray());

    public static ProtoTestAttachment FromFile(
        string filePath,
        string? name = null,
        string mediaType = "application/octet-stream",
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The test attachment file does not exist.", fullPath);
        }

        return new ProtoTestAttachment(name ?? Path.GetFileName(fullPath), mediaType, description, fullPath, null);
    }

    public ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
        => _content is not null
            ? ValueTask.FromResult(_content.ToArray())
            : new ValueTask<byte[]>(File.ReadAllBytesAsync(FilePath!, cancellationToken));

    /// <summary>
    /// Returns the original file path or writes in-memory content to a durable temporary file.
    /// </summary>
    public ValueTask<string> MaterializeFileAsync(CancellationToken cancellationToken = default)
    {
        if (FilePath is not null)
        {
            return ValueTask.FromResult(FilePath);
        }

        lock (_materializationGate)
        {
            _materialization ??= MaterializeContentAsync(CancellationToken.None);
            return cancellationToken.CanBeCanceled
                ? new ValueTask<string>(_materialization.WaitAsync(cancellationToken))
                : new ValueTask<string>(_materialization);
        }
    }

    private async Task<string> MaterializeContentAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoTest", "attachments");
        Directory.CreateDirectory(directory);

        var fileName = SanitizeFileName(Name);
        if (!Path.HasExtension(fileName))
        {
            fileName += ExtensionFor(MediaType);
        }

        var path = Path.Combine(directory, $"{Guid.NewGuid():N}-{fileName}");
        await File.WriteAllBytesAsync(path, _content!, cancellationToken);
        return path;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "attachment" : sanitized;
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "application/json" => ".json",
        "application/xml" or "text/xml" => ".xml",
        "text/html" => ".html",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        _ => ".bin"
    };
}
