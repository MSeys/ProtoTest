# REST demo

The REST demo is the complete runnable example for ProtoTest. It uses WireMock.Net as an external-style API and demonstrates:

- suite-level server and client setup;
- named REST clients;
- class-level bearer-token authentication;
- fluent authentication for individual requests;
- response status and shape matching;
- REST coverage collection.

## Run it

From the repository root:

```bash
dotnet test samples/ProtoTest.Rest.Demo/ProtoTest.Rest.Demo.csproj
```

## Source files

- [`Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/Setup.cs) configures WireMock.Net, application configuration, named clients, and coverage.
- [`OrderApiTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/OrderApiTests.cs) demonstrates class-level authentication and response matching.
- [`InventoryApiTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/InventoryApiTests.cs) demonstrates fluent authentication.
- [`appsettings.json`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Rest.Demo/appsettings.json) contains the client configuration shape.

For the concepts behind the example, read the [REST integration guide](../integrations/rest.md) and the [with-and-without comparison](../comparisons/with-and-without-prototest.md).
