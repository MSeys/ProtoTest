namespace ProtoTest.Testcontainers;

/// <summary>
/// The outcome of starting a container without throwing: the started resource, or the reason it could
/// not start. A machine without a container runtime should be able to fall back or skip rather than
/// fail the run, so a fixture decides on the result before registering the container.
/// </summary>
public readonly record struct ContainerStartResult<TResource>(TResource? Resource, string? Error)
    where TResource : class
{
    /// <summary>Whether the container started and <see cref="Resource"/> is the live one.</summary>
    public bool Started => Resource is not null;
}
