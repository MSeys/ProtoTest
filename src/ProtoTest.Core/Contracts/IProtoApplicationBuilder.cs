namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures one named application under test. Integrations extend it, for example with
/// <c>app.AddRest(rest =&gt; rest.AddClient("Orders"))</c>, to expose the protocols and clients the
/// application supports. The first client registered for a protocol becomes that protocol's default.
/// </summary>
public interface IProtoApplicationBuilder
{
    /// <summary>Gets the application name, matching <c>[Application(name)]</c> and configuration.</summary>
    string ApplicationName { get; }

    /// <summary>Gets the service collection the application's clients are registered into.</summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Gets the host builder the application is declared on, so an integration reached from inside
    /// <c>AddApplication</c> registers run-level pieces - a nested worker - through the same entries it
    /// uses outside.
    /// </summary>
    IProtoHostBuilder Host { get; }

    /// <summary>
    /// Gets the ordered provider chain that resolves the application's address - the derived
    /// <c>ProtoTest:Applications:{name}:BaseUrl</c> key. Providers are added in priority order
    /// (<c>UseConfigured()</c>, <c>UseInProcess&lt;TProgram&gt;()</c>, <c>UseLoopback(...)</c>, an
    /// integration's <c>UseAspireResource(...)</c>); the first whose condition holds serves the
    /// application and only its capabilities are declared. An application that declares no provider
    /// keeps the plain configured-address behavior.
    /// </summary>
    IProtoProviderChainBuilder Providers { get; }

    /// <summary>Records a named client of a protocol so accessors can resolve it by application.</summary>
    void RegisterClient(string protocolName, string clientName);
}
