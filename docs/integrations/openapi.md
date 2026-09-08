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
		  "Path": "contracts/orders.json"
		}
	  }
	}
  }
}
```

The supported keys are:

- `ProtoTest:Clients:{name}:OpenApi:Url`
- `ProtoTest:Clients:{name}:OpenApi:Path`
- `ProtoTest:Clients:{name}:OpenApi:Specification`

A URL can be relative to the configured client base URL. A path can point to a JSON or YAML file.

## Register the collector

Compose the collector with the named REST target:

```csharp
builder.AddRest(rest => rest
	.AddClient("Orders")
	.WithCoverage<OpenApiCoverageCollector>());
```

`OpenApiCoverageCollector` reads the named client's OpenAPI configuration through dependency injection. It also supports direct construction when a test host needs to supply a specification programmatically.

## What is measured

REST requests are matched to OpenAPI operations and can produce coverage items for:

- endpoints and HTTP methods;
- response status codes;
- response properties, including nested properties;
- properties observed by `ShouldMatchShape`.

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
	var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response
		.ShouldHaveStatus(HttpStatusCode.OK)
		.ShouldMatchShape(new { id = 42, status = "confirmed" });
}
```

The OpenAPI tests in [`tests/ProtoTest.OpenApi.Tests`](../../tests/ProtoTest.OpenApi.Tests) cover loading, route matching, status coverage, and nested response properties.
