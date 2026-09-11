# ProtoTest.SampleApp.GraphQLDemo

End-to-end GraphQL scenarios against the shared multi-tenant SampleApp. The tests reuse REST
for scenario provisioning, then exercise GraphQL mutations, variables, filtering, ordering,
paging, authentication, shape matching, and SDL coverage.

```powershell
dotnet test samples/ProtoTest.SampleApp.GraphQLDemo
```
