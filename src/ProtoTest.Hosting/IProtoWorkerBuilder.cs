namespace ProtoTest.Hosting;

using ProtoTest.Core;

/// <summary>
/// Builds one worker's provider chain: the providers decide where the worker runs - hosted in this
/// process, run by the environment the application resolves to, or started as infrastructure. Add
/// providers in priority order; the first whose condition holds serves the worker.
/// </summary>
/// <example>
/// <code>
/// builder.AddApplication("Api", app => app
///     .UseConfigured()
///     .UseInProcess&lt;Program&gt;()
///     .AddWorkerHost&lt;BillingWorker&gt;("Billing", worker => worker
///         .UseEnvironment()   // the environment that runs the application runs its worker
///         .UseHost()));       // otherwise the run hosts the worker's entry point
/// </code>
/// </example>
public interface IProtoWorkerBuilder
{
    /// <summary>Gets the worker's name, in configuration, the registry and diagnostic messages.</summary>
    string WorkerName { get; }

    /// <summary>Adds a provider to the end of the worker's chain; a later provider is lower priority.</summary>
    IProtoWorkerBuilder Use(IProtoTargetProvider provider);

    /// <summary>
    /// Adds the provider that leaves the worker to the environment: it is available when the
    /// application the worker belongs to is served by a provider that does not run it in-process
    /// (configured, loopback, AppHost or container), so the environment already runs the worker and
    /// the suite must not start a second consumer. Starts nothing; a top-level worker has no
    /// application, so this provider is never available there.
    /// </summary>
    IProtoWorkerBuilder UseEnvironment();

    /// <summary>
    /// Adds the provider that hosts the worker's own entry point in this process, as the default
    /// fallback. It is the only provider that bridges the test clock and lets
    /// <c>Proto.Context.Host&lt;TProgram&gt;()</c> resolve the running host.
    /// </summary>
    IProtoWorkerBuilder UseHost();
}
