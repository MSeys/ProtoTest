# ProtoTest.Core

The runner-independent foundation of ProtoTest. It provides the suite and test lifecycle, dependency injection, `Proto.Context`, typed test state, named clients, attachments, observations, collectors, and reporting contracts.

```bash
dotnet add package ProtoTest.Core --prerelease
```

Most test projects should install a framework adapter such as `ProtoTest.NUnit` or `ProtoTest.Xunit3`, which brings in Core transitively.

```csharp
public sealed class EnvironmentContext(Uri baseUri) : IProtoContext
{
    public Uri BaseUri { get; } = baseUri;
}

var environment = Proto.Context.Resolve<EnvironmentContext>();
```

See the [ProtoTest repository](https://github.com/matthiasseys/ProtoTest) for lifecycle, extension, and integration guides.
