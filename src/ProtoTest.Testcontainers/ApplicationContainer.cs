namespace ProtoTest.Testcontainers;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using ProtoTest.Core;

/// <summary>
/// An application under test running in its own container image: the host starts it as run
/// infrastructure, and the mapped address it serves on is published as the application's
/// <c>BaseUrl</c>, so the application's REST clients, browser sessions and the readiness probe all
/// resolve that one container. Register it with <c>AddInfrastructure(container, container.BaseUrlKey)</c>.
/// </summary>
/// <remarks>
/// The helper assumes nothing about the image beyond the port it listens on; the published address is
/// <c>http://{hostname}:{mapped port}</c>, and the default readiness check waits for that port to
/// accept a connection. The <c>configure</c> callback can add container build options (an environment,
/// a command, a Testcontainers wait strategy), and <c>AddHttpReadiness(application, path)</c> adds an
/// HTTP-level wait against the published address. A run that configures
/// <c>ProtoTest:Applications:{application}:BaseUrl</c> satisfies the declared key, so
/// <c>AddInfrastructure</c> skips the container and points at that environment instead.
/// </remarks>
public sealed class ApplicationContainer : ProtoContainerResource<IContainer>
{
    private readonly string _image;

    private ApplicationContainer(string application, string image, int port, Action<ContainerBuilder>? configure)
        : base(
            () =>
            {
                var builder = new ContainerBuilder(image)
                    .WithPortBinding(port, assignRandomHostPort: true);
                configure?.Invoke(builder);
                return builder.Build();
            },
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            container => $"http://{container.Hostname}:{container.GetMappedPublicPort(port)}")
    {
        Application = application;
        _image = image;
        ReadyWhenTcp(
            $"{application} accepts connections",
            port,
            (container, readinessPort) => (container.Hostname, container.GetMappedPublicPort(readinessPort)));
    }

    /// <summary>Gets the application this container serves.</summary>
    public string Application { get; }

    /// <summary>Gets the key the started container fills with the mapped address.</summary>
    public string BaseUrlKey => $"{ProtoApplication.SectionPath}:{Application}:BaseUrl";

    public override string Id => $"application:container:{Application}";

    public override string Kind => "application";

    public override string Description => $"{Application} in container image '{_image}'";

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public static ApplicationContainer Container(
        string application,
        string image,
        int port,
        Action<ContainerBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        return new(application, image, port, configure);
    }

    /// <summary>Starts a container now, or throws with the reason it could not start.</summary>
    public static ApplicationContainer Start(
        string application,
        string image,
        int port,
        Action<ContainerBuilder>? configure = null)
    {
        var result = TryStart(application, image, port, configure);
        return result.Resource
            ?? throw new InvalidOperationException(
                $"The application container for '{application}' did not start: {result.Error}");
    }

    /// <summary>
    /// Starts a container, reporting why it could not start instead of throwing - a machine without a
    /// container runtime should be able to fall back or skip rather than fail the run.
    /// </summary>
    public static ContainerStartResult<ApplicationContainer> TryStart(
        string application,
        string image,
        int port,
        Action<ContainerBuilder>? configure = null)
        => TryStartContainer(Container(application, image, port, configure));
}
