# REST demo

The REST demo is the complete runnable example for ProtoTest. It uses WireMock.Net as an external-style API and demonstrates:

- suite-level server and client setup;
- named REST clients;
- class-level bearer-token authentication;
- fluent authentication for individual requests;
- a per-test SaaS environment and user context;
- user provisioning with `WithoutAuth` and a context-aware authenticator;
- request headers and query parameters;
- response status and shape matching;
- attachments, REST and OpenAPI coverage;
- end-of-run JSON and self-contained HTML reports.

## Run it

From the repository root:

```bash
dotnet test samples/ProtoTest.Rest.Demo/ProtoTest.Rest.Demo.csproj
```

## Source files

- [`Setup.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/Setup.cs) configures WireMock.Net, application configuration, named clients, and coverage.
- [`OrderApiTests.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/OrderApiTests.cs) demonstrates class-level authentication and response matching.
- [`InventoryApiTests.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/InventoryApiTests.cs) demonstrates fluent authentication.
- [`SaasEnvironmentTests.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/SaasEnvironmentTests.cs) demonstrates per-test environments, provisioned users, roles, and contextual authentication.
- [`orders.openapi.json`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/orders.openapi.json) is the contract used for OpenAPI coverage.
- [`appsettings.json`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/appsettings.json) contains the client configuration shape.

For the concepts behind the example, read the [REST integration guide](../integrations/rest.md) and the [with-and-without comparison](../comparisons/with-and-without-prototest.md).

After the run, inspect `TestResults/ProtoTest.Rest.Demo/report.json` and `report.html` below the test working directory (normally the demo's build output directory). Request, response, and expected-shape artifacts are also attached to the individual NUnit results.
The contract intentionally contains one untested `500` response so the report demonstrates uncovered contract behavior instead of always showing 100%.
