using System.Runtime.CompilerServices;

// Protocol integrations wire their builders up through the public builder surface; only the focused
// tests reach the internals directly.
[assembly: InternalsVisibleTo("ProtoTest.Http.Tests")]
[assembly: InternalsVisibleTo("ProtoTest.Rest.Tests")]
[assembly: InternalsVisibleTo("ProtoTest.GraphQL.Tests")]
