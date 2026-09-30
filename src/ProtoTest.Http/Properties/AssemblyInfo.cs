using System.Runtime.CompilerServices;

// Protocol integrations wire their builders up through the public builder surface; only the focused
// tests reach the internals directly. ProtoTest.Rest also shares the media type classification.
[assembly: InternalsVisibleTo("ProtoTest.Rest")]
[assembly: InternalsVisibleTo("ProtoTest.Http.Tests")]
[assembly: InternalsVisibleTo("ProtoTest.Rest.Tests")]
[assembly: InternalsVisibleTo("ProtoTest.GraphQL.Tests")]
