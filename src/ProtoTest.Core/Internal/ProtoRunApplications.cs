namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The application clients a run phase opened: an application's address when one is configured or
/// published, otherwise the in-process transport its client initializer provides. Each is recorded in the
/// run trace and released with the phase.
/// </summary>
internal sealed class ProtoRunApplications(IServiceProvider services, ProtoTraceSession trace) : IAsyncDisposable
{
    private readonly List<ProtoRunApplicationClient> _opened = [];

    public async ValueTask<HttpClient> ClientAsync(ProtoRunSetupContext context, string applicationName)
    {
        var address = ProtoApplication.ResolveSetting(
            context.Configuration,
            context.Settings,
            ProtoApplication.SettingsKey(applicationName, "BaseUrl"));
        ProtoRunApplicationClient opened;
        string mode;
        if (!string.IsNullOrWhiteSpace(address))
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var baseAddress)
                || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Application '{applicationName}' has the base address '{address}', which is not an absolute HTTP or HTTPS URL. " +
                    $"Fix '{ProtoApplication.SectionPath}:{applicationName}:BaseUrl'.");
            }

            opened = new ProtoRunApplicationClient(new HttpClient { BaseAddress = baseAddress });
            mode = "address";
        }
        else if (Transport(applicationName) is { } transport)
        {
            opened = await transport.OpenRunClientAsync(context);
            mode = "in-process";
        }
        else
        {
            throw new InvalidOperationException(
                $"Application '{applicationName}' has no address and no in-process server, so the run cannot reach it. " +
                $"Configure '{ProtoApplication.SectionPath}:{applicationName}:BaseUrl' or register its in-process server.");
        }

        _opened.Add(opened);
        trace.RunWriter.WriteEvent(
            "run.application.client",
            $"Run client · {applicationName}",
            ProtoCoreDiagnostics.TraceSource,
            phase: ProtoTracePhase.Run,
            outcome: ProtoTraceOutcome.Succeeded,
            attributes: new Dictionary<string, string?>
            {
                ["application.name"] = applicationName,
                ["application.mode"] = mode,
                ["application.address"] = opened.Client.BaseAddress?.ToString()
            });
        return opened.Client;
    }

    // Client initializers serve the application's tests; one that can also serve the run says so.
    private IProtoRunApplicationTransport? Transport(string applicationName)
        => services.GetServices<IProtoClientInitializer>()
            .OfType<IProtoRunApplicationTransport>()
            .FirstOrDefault(transport => string.Equals(transport.ApplicationName, applicationName, StringComparison.OrdinalIgnoreCase));

    public async ValueTask DisposeAsync()
    {
        foreach (var opened in Enumerable.Reverse(_opened))
        {
            opened.Client.Dispose();
            if (opened.Owned is not null)
            {
                await opened.Owned.DisposeAsync();
            }
        }

        _opened.Clear();
    }
}

/// <summary>A provider with no services, for a run context built outside a run.</summary>
internal sealed class ProtoEmptyServices : IServiceProvider
{
    public static readonly ProtoEmptyServices Instance = new();

    public object? GetService(Type serviceType) => null;
}
