# ProtoTest.SampleApp.Domain

The Northstar domain, kept separate from the ASP.NET Core host that exposes it.

It contains the store that owns organizations, projects, environments, deployments, usage, invoices and the webhook outbox; the plan catalogue and its entitlements; the domain exceptions; and the event bus that backs GraphQL subscriptions. It depends on nothing but the BCL and the contracts.

The separation is what makes the demo interesting: the application is one host of this domain, and a test can compose the same domain itself - in-process or against a published environment's database - to arrange data through real domain logic rather than only through the API.

```csharp
builder.ConfigureServices(services => services.AddNorthstarDomain());
```

The sample keeps its domain types internal and grants access to the app and the demo through `InternalsVisibleTo`. A real domain would expose them publicly.
