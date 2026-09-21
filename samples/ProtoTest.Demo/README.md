# ProtoTest.Demo

A larger test suite for Northstar, the multi-tenant ASP.NET Core application included in this repository.

The starter template is meant to be small. This demo is where I combine REST, GraphQL, gRPC, messaging, SQL, browser tests and generated workbooks in the same application.

## Run it

```bash
dotnet test samples/ProtoTest.Demo
```

The run writes its trace and reports under `TestResults/ProtoTest.Demo`.

The default run uses SQLite. PostgreSQL and RabbitMQ containers are optional, and browser journeys are skipped when the Northstar Vue application has not been built.

## Where should I start?

- `Setup.cs` composes the application, integrations, tracing and reporting once for the suite.
- `CrossLayerJourneys.cs` verifies the same scenario through more than one interface.
- `DiagnosticsShowcase.cs` contains opt-in failures used by the bundled demo trace.
- `samples/Northstar.ProtoTest` contains the application-specific attributes, authentication, page objects and data provisioners. This is what you would create for your own application to integrate your needs into ProtoTest.

## Learn more

- [Interactive trace](https://trace.prototest.dev/?demo=1)
- [Recipes](https://prototest.dev/docs/recipes/overview)
- [ProtoTest documentation](https://prototest.dev/)
