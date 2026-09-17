---
sidebar_position: 2
title: xUnit v2
---

# xUnit v2

```bash
dotnet add package ProtoTest.Xunit
```

## Assembly setup

`ProtoTestAssembly` implements xUnit's `IAsyncLifetime`, so it works as a collection fixture. Every test class that needs ProtoTest joins that collection.

```csharp
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using Xunit;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddRest(rest => rest.AddClient("Api", "https://api.example.test/"));
}

[CollectionDefinition(Name)]
public class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}
```

## Writing a test

`[ProtoTest]` is a `BeforeAfterTestAttribute`, **not** a `FactAttribute` — you need both it and `[Fact]` (or `[Theory]`).

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using System.Net;
using Xunit;

[Collection(ProtoTestCollection.Name)]
public class OrderTests
{
    [Fact]
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest("Api").GetAsync("/api/orders");
        response.ShouldHaveStatus(HttpStatusCode.OK);
    }
}
```

## Things to know

**The recorded outcome is always `Unknown`.** xUnit v2 gives a `BeforeAfterTestAttribute` no access to the test result, so ProtoTest calls `CompleteTestAsync()` without one. Your test still passes or fails normally in xUnit — but the trace and reports can't show which. If that matters, use [xUnit v3](./xunit3).

**Attachments are written to disk, not attached.** xUnit v2 has no attachment API, so each artifact is materialised to a file and its path written to the console:

```
ProtoTest attachment 'rest-01-response': /path/to/TestResults/.../rest-01-response.json
```

**The lifecycle is sync-over-async.** `Before`/`After` call `.GetAwaiter().GetResult()` because the xUnit v2 hook is synchronous.

**Forgetting `[Collection]` breaks the host.** Without it the fixture never runs, and the first `Proto.Context` access throws `InvalidOperationException: ProtoHost is not initialized.`
