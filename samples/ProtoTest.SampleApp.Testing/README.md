# ProtoTest.SampleApp.Testing

Reusable scenario infrastructure for every integration demo targeting `ProtoTest.SampleApp`.

It currently provides `[SampleEnvironment]`, `[SampleUser]`, their typed contexts, and
`SampleUserAuthenticator`. Environment cleanup is automatic and the ordering required by
provisioning is encapsulated inside the attributes instead of repeated in test classes.
