namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;

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
    CancellationToken CancellationToken);
