namespace ProtoTest.GraphQL;

using HotChocolate.Language;

internal sealed record GraphQLBuiltOperation(string DocumentText, DocumentNode Document, string Type, string? Name);
