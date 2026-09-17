namespace ProtoTest.GraphQL;

using System.Text.Json;

public sealed record GraphQLError(string Message, IReadOnlyList<object> Path, string? Code, JsonElement? Extensions);
