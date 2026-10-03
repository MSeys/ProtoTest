namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core.Internal;

/// <summary>
/// The run's collected state a run setup step reads: the settings started infrastructure published
/// before the step's registration position, the suite's configuration it can fall back to, and the
/// run's start cancellation token. Read <see cref="Settings"/> first and <see cref="Configuration"/>
/// second - the precedence every address reader follows - so a container the environment replaced with
/// a configured address is skipped and the configuration value still reaches the step.
/// </summary>
public sealed record ProtoRunSetupContext(
    ProtoInfrastructureSettings Settings,
    IConfiguration Configuration,
    CancellationToken CancellationToken)
{
    /// <summary>The run's services; empty for a context built outside a run.</summary>
    public IServiceProvider Services { get; internal init; } = ProtoEmptyServices.Instance;

    internal ProtoRunApplications? Applications { get; init; }

    /// <summary>
    /// An HTTP client for an application, released when the phase ends: its address when one is configured
    /// or published, otherwise its in-process server. Available in
    /// <see cref="IProtoRunHook.AfterInfrastructureAsync"/>, once every piece the application reads started.
    /// </summary>
    public ValueTask<HttpClient> ApplicationClientAsync(string applicationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return Applications is { } applications
            ? applications.ClientAsync(this, applicationName)
            : throw new InvalidOperationException(
                $"Application '{applicationName}' is reachable once every infrastructure piece started: " +
                "read it in IProtoRunHook.AfterInfrastructureAsync.");
    }
}
