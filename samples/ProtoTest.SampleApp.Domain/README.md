# ProtoTest.SampleApp.Domain

The Northstar domain, kept separate from the ASP.NET Core host that exposes it.

It holds the store (organizations, projects, environments, deployments, usage, invoices, webhook outbox), the plan catalogue and its entitlements, the domain exceptions, and the event bus behind the GraphQL subscriptions. It depends only on the BCL and the contracts package.

The separation is what makes the sample teachable: the application is one host of this domain, and a test can compose the same domain itself, in process or against a published environment's database, to arrange data through real domain logic rather than only through the API.

```csharp
builder.ConfigureServices(services => services.AddNorthstarDomain());
```

The sample keeps its domain types internal and grants access to the app and the Learning demo suite through `InternalsVisibleTo`. A real domain would expose them publicly. This project is intentionally not packaged.
