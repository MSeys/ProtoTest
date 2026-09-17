namespace ProtoTest.GraphQL;

using HotChocolate.Language;

public static class Gql
{
    /// <summary>Marks a scalar field in an anonymous selection shape.</summary>
    public static GraphQLFieldSelection Field { get; } = new();
    public static GraphQLEnum Enum(string value) => new(value);
    public static GraphQLVariableReference Var(string name) => new(name);
    /// <summary>Declares and supplies a variable for a shape-driven field argument.</summary>
    public static GraphQLVariableValue Variable(string type, object? value) => new(type, value);
    public static GraphQLVariableValue Variable(GraphQLTypeReference type, object? value)
        => new(type?.Syntax ?? throw new ArgumentNullException(nameof(type)), value);
    public static GraphQLUpload Upload(
        ReadOnlyMemory<byte> content,
        string fileName,
        string contentType = "application/octet-stream")
    {
        var bytes = content.ToArray();
        return new GraphQLUpload(() => new MemoryStream(bytes, writable: false), fileName, contentType);
    }
    public static GraphQLUpload Upload(
        Func<Stream> openRead,
        string fileName,
        string contentType = "application/octet-stream")
        => new(openRead, fileName, contentType);
}

public sealed class GraphQLFieldSelection
{
    internal GraphQLFieldSelection() { }
}

public sealed record GraphQLEnum(string Value);
public sealed record GraphQLVariableReference(string Name);

public sealed record GraphQLVariableValue
{
    public GraphQLVariableValue(string type, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        _ = Utf8GraphQLParser.Parse($"query Variable($value: {type}) {{ __typename }}");
        Type = type;
        Value = value;
    }

    public string Type { get; }
    public object? Value { get; }
}

public sealed record GraphQLTypeReference(string Syntax)
{
    public GraphQLTypeReference NonNull() => new($"{Syntax}!");
    public GraphQLTypeReference List() => new($"[{Syntax}]");
}

public static class GqlType
{
    public static GraphQLTypeReference Id { get; } = new("ID");
    public static GraphQLTypeReference String { get; } = new("String");
    public static GraphQLTypeReference Int { get; } = new("Int");
    public static GraphQLTypeReference Float { get; } = new("Float");
    public static GraphQLTypeReference Boolean { get; } = new("Boolean");
    public static GraphQLTypeReference Upload { get; } = new("Upload");
    public static GraphQLTypeReference Named(string name) => new(name);
}
