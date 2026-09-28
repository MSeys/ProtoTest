namespace ProtoTest.Extensibility.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// A minimal target provider written against the public surface only - this project has no
/// InternalsVisibleTo grant, so it compiles exactly like a third-party provider package. It serves a
/// store target with a connection string when its probe says the environment is available, declares
/// the store capability it serves, and contributes a marker service only while it wins its target.
/// </summary>
public sealed class MinimalTargetProvider : IProtoTargetProvider
{
    private readonly MinimalInfrastructure _infrastructure = new();

    public string Name => "minimal";

    public IProtoProviderCondition? Condition { get; } =
        ProtoProviderConditions.Available("The minimal provider is available", () => true);

    public IProtoInfrastructure? Infrastructure => _infrastructure;

    public IReadOnlyList<ProtoCapabilityDescriptor> Capabilities { get; } =
        [new ProtoCapabilityDescriptor("Minimal store", ProtoCapabilityKinds.Store, "Extensibility")];

    /// <summary>Whether the piece started; a losing provider's piece never does.</summary>
    public bool Started => _infrastructure.StartCount > 0;

    /// <summary>
    /// Registers the service only the winner may expose: a losing provider contributes nothing, so a
    /// consumer's absence is the same fact as the provider losing.
    /// </summary>
    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(new MinimalWinnerMarker());
    }

    /// <summary>The service <see cref="ConfigureServices"/> contributes to a winning provider.</summary>
    public sealed class MinimalWinnerMarker;

    private sealed class MinimalInfrastructure : IProtoConnectionInfrastructure
    {
        public string Id => "minimal:store";

        public string Kind => "database";

        public string Description => "Minimal store";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public string ConnectionString => "Host=minimal";

        public int StartCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            StartCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
