# ProtoTest.SampleApp.Testing

Reusable scenario infrastructure for every integration demo targeting Northstar.

It provides `[NorthstarTenant]` (provisions and removes an isolated organization, optionally on a given plan), `[SignedInAs(role)]` (acts as the owner or as a provisioned member with that role), their typed contexts, `NorthstarAuthenticator`, plus `NorthstarDataDefaults` and `NorthstarMemberProvisioner`. Ordering and cleanup live inside the attributes instead of being repeated in each test class.
