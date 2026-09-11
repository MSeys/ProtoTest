# OpenAPI integration

`ProtoTest.OpenApi` turns REST request hits into contract coverage. It can load an OpenAPI document from a URL, a local path, raw JSON/YAML content, or a resolved `OpenApiDocument`.

## Configure a specification

OpenAPI configuration is associated with a named client:

```json
{
  "ProtoTest": {
	"Clients": {
	  "Orders": {
		"BaseUrl": "https://localhost:5001",
		"OpenApi": {
		  "Specification": "contracts/orders.json"
		}
	  }
	}
  }
}
```

`ProtoTest:Clients:{name}:OpenApi:Specification` is the single canonical setting. Its
value can be inline JSON/YAML, a local file, an absolute URL, or a URL relative to the
configured client `BaseUrl`.

## Register the collector

Compose the collector with the named REST target:

```csharp
builder.AddRest(rest => rest
	.AddClient("Orders")
	.WithCollector<OpenApiCoverageCollector>());
```

`OpenApiCoverageCollector` reads the named client's OpenAPI configuration through dependency injection. It also supports direct construction when a test host needs to supply a specification programmatically.

## What is measured

REST requests are matched to OpenAPI operations and can produce coverage items for:

- endpoints and HTTP methods;
- response status codes;
- response properties, including nested properties;
- properties observed by `ShouldMatchShape`.

Exact response codes, ranges such as `2XX`, and `default` responses are tracked separately. Property coverage belongs to the response that was actually asserted. Referenced and composed schemas (`allOf`, `oneOf`, and `anyOf`) are traversed, and array paths use `[]` in report identifiers. Request matching accepts route templates, concrete paths, absolute URLs, and URLs containing query strings.

The request test remains a normal REST test. Coverage is collected as a side effect of the existing request and assertion APIs.

## Example test

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[ProtoTest]
[RestClient("Orders")]
public async Task GetOrder_ExercisesDocumentedEndpoint()
{
	using var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response
		.ShouldHaveStatus(HttpStatusCode.OK)
		.ShouldMatchShape(new { id = 42, status = "confirmed" });
}
```

The OpenAPI tests in [`tests/ProtoTest.OpenApi.Tests`](../../tests/ProtoTest.OpenApi.Tests) cover loading, route matching, status coverage, and nested response properties.

The runnable [REST demo](../examples/rest-demo.md) combines `OpenApiCoverageCollector` with both JSON and HTML reporting.
