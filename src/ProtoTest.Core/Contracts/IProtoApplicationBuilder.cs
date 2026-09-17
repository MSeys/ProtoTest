namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Configures one named application under test. Integrations extend it — for example
/// <c>app.AddRest(rest =&gt; rest.AddClient("Orders"))</c> — to expose the protocols and clients the
/// application supports. The first client registered for a protocol becomes that protocol's default.
/// </summary>
public interface IProtoApplicationBuilder
{
    /// <summary>Gets the application name, matching <c>[Application(name)]</c> and configuration.</summary>
    string ApplicationName { get; }

    /// <summary>Gets the service collection the application's clients are registered into.</summary>
    IServiceCollection Services { get; }

    /// <summary>Records a named client of a protocol so accessors can resolve it by application.</summary>
    void RegisterClient(string protocolName, string clientName);
}
