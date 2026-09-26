namespace ProtoTest.AspNetCore.Web.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Run infrastructure that hosts the application under test on its own loopback listener inside this
/// test process and publishes the address the listener bound as the application's <c>BaseUrl</c> - the
/// documented recipe in docs/docs/recipes/api-then-browser.md. Registered with the address key it
/// fills, so an environment that configures that key skips the listener and the browser talks to that
/// environment instead.
/// </summary>
internal sealed class LoopbackApplication : IProtoSettingsInfrastructure
{
    private readonly Func<string[], WebApplication> _create;
    private WebApplication? _application;
    private string _baseUrl = string.Empty;

    internal LoopbackApplication(string applicationName, Func<string[], WebApplication> create)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(create);
        ApplicationName = applicationName;
        _create = create;
    }

    internal string ApplicationName { get; }

    internal string BaseUrlKey => $"{ProtoApplication.SectionPath}:{ApplicationName}:BaseUrl";

    public string Id => $"application:loopback:{ApplicationName}";

    public string Kind => "application";

    public string Description => $"{ApplicationName} on a loopback listener";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [BaseUrlKey] = _baseUrl
    };

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        // Port 0 lets the OS pick a free port, and the bound address is read back from the listener
        // rather than guessed, so suites never collide over a fixed port.
        var application = _create(["--urls", "http://127.0.0.1:0"]);
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        _application = application;
        _baseUrl = application.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses
            .First();
    }

    public async ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        if (_application is not { } application)
        {
            return;
        }

        _application = null;
        await application.DisposeAsync().ConfigureAwait(false);
    }
}
