# ProtoTest.Http

Shared HTTP foundations for protocol integrations. It owns named `HttpClient`
initialization from a target application's `ProtoTest:Applications:{application}:BaseUrl`
(or its `Endpoints:{client}` and in-process server), per-test base-address registrations,
bounded response buffering, and the common oversized-response exception.

Application tests normally reference `ProtoTest.Rest` or `ProtoTest.GraphQL`; this
package is primarily an extension point for integration authors.
