# ProtoTest.OpenApi

Creates contract coverage from ProtoTest REST observations and an OpenAPI document.

```bash
dotnet add package ProtoTest.OpenApi
```

Register `OpenApiCoverageCollector` on a REST target and point it at an OpenAPI file, URL or document. It reports which endpoints, responses and response properties were reached or asserted.

This package reports coverage. It does not validate requests or responses against the specification.

## Learn more

- [OpenAPI integration](https://prototest.dev/docs/integrations/openapi)
- [Coverage](https://prototest.dev/docs/observability/coverage)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
