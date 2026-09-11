# ProtoTest.Http

Shared HTTP foundations for protocol integrations. It owns named `HttpClient`
initialization from `ProtoTest:Clients:{name}:BaseUrl`, per-test base-address
registrations, bounded response buffering, and the common oversized-response exception.

Application tests normally reference `ProtoTest.Rest` or `ProtoTest.GraphQL`; this
package is primarily an extension point for integration authors.
