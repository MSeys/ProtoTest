namespace ProtoTest.Testcontainers;

using Docker.DotNet;
using DotNet.Testcontainers.Configurations;
using ProtoTest.Core;

/// <summary>
/// The one availability probe for containers: whether the Docker daemon answers. A container provider
/// condition reads it, so a machine without a container runtime skips the provider instead of failing
/// the build.
/// </summary>
public static class DockerProbe
{
    /// <summary>
    /// Returns whether the Docker daemon answers through the same endpoint configuration
    /// Testcontainers uses, so the probe agrees with the container that would start.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            using var client = TestcontainersSettings.OS.DockerEndpointAuthConfig
                .GetDockerClientBuilder(Guid.NewGuid())
                .Build();
            client.System.PingAsync().GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            // A probe that cannot answer means the runtime is not available: that is the false answer
            // the condition wants, not an exception a build should carry.
            return false;
        }
    }

    /// <summary>The condition a container provider registers: available when Docker answers.</summary>
    public static IProtoProviderCondition Condition { get; } =
        ProtoProviderConditions.Available("Docker is available", IsAvailable);
}

/// <summary>Registers a container piece as a target provider.</summary>
public static class ProtoContainerProviderExtensions
{
    /// <summary>
    /// Adds the container as a provider of the target's chain: it serves when the Docker daemon
    /// answers, starts at its registration position and fills the target's declared keys. Prefer a
    /// configured or AppHost provider earlier in the chain, so a run pointed at an existing
    /// environment never starts a container the environment already provides.
    /// </summary>
    /// <param name="chain">The target's provider chain.</param>
    /// <param name="container">
    /// The container piece, for example <c>PostgresDatabase.Container()</c>. It declares the target's
    /// keys and is started and released like any infrastructure piece.
    /// </param>
    public static IProtoProviderChainBuilder UseContainer(
        this IProtoProviderChainBuilder chain,
        IProtoInfrastructure container)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(container);
        return chain.Use(new ProtoTargetProvider($"container:{container.Id}", container, DockerProbe.Condition));
    }
}
