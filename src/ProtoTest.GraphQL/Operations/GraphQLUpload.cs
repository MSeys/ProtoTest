namespace ProtoTest.GraphQL;

public sealed class GraphQLUpload
{
    internal GraphQLUpload(Func<Stream> openRead, string fileName, string contentType)
    {
        OpenRead = openRead ?? throw new ArgumentNullException(nameof(openRead));
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        FileName = fileName;
        ContentType = contentType;
    }

    internal Func<Stream> OpenRead { get; }
    public string FileName { get; }
    public string ContentType { get; }
}
