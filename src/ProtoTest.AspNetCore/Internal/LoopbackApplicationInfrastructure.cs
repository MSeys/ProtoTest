namespace ProtoTest.AspNetCore.Internal;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Run infrastructure that hosts a hand-built <see cref="WebApplication"/> on its own loopback
/// listener inside the test process and publishes the address the listener bound as the
/// application's <c>BaseUrl</c>.
/// </summary>
internal sealed class LoopbackApplicationInfrastructure : IProtoConfiguredInfrastructure, IProtoSettingsInfrastructure
{
    private readonly Func<string[], WebApplication> _create;
    private WebApplication? _application;
    private string _baseUrl = string.Empty;

    internal LoopbackApplicationInfrastructure(string applicationName, Func<string[], WebApplication> createApp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(createApp);
        ApplicationName = applicationName;
        _create = createApp;
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

    public async ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A hand-built application receives no infrastructure settings from the host, so the run's
        // collected configuration travels as command-line arguments the factory's
        // WebApplication.CreateBuilder(args) reads: the suite's configuration first, then the values
        // the pieces started before this one published, so a container address wins. Port 0 rides
        // last, so the OS picks a free port and nothing the run provides can replace it.
        var arguments = ComposeArguments(context);
        var application = _create(arguments);
        try
        {
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            _baseUrl = application.Services
                .GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses
                .First();
            _application = application;
        }
        catch
        {
            await application.DisposeAsync().ConfigureAwait(false);
            throw;
        }
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

    private static string[] ComposeArguments(ProtoInfrastructureContext context)
    {
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in context.Configuration.AsEnumerable())
        {
            merged[key] = value;
        }

        foreach (var (key, value) in context.Settings.Values)
        {
            merged[key] = value;
        }

        var arguments = new List<string>(merged.Count + 2);
        foreach (var (key, value) in merged)
        {
            if (value is not null)
            {
                arguments.Add($"--{key}={value}");
            }
        }

        arguments.Add("--urls");
        arguments.Add("http://127.0.0.1:0");
        return [.. arguments];
    }
}
