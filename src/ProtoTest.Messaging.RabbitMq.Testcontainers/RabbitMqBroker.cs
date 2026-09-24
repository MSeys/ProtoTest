namespace ProtoTest.Messaging.RabbitMq.Testcontainers;

// global:: because this assembly's own namespace ends in Testcontainers.
using global::Testcontainers.RabbitMq;
using ProtoTest.Testcontainers;

/// <summary>
/// A RabbitMQ container owned by the whole run: started once for the suite and released when the host
/// is disposed, after the run has stopped and the reports are written. Register it with
/// <c>AddInfrastructure</c> and hand its connection string to the application and to the tests so both
/// work against the same broker - the same shape as the PostgreSQL container.
/// </summary>
public sealed class RabbitMqBroker : ProtoContainerResource<RabbitMqContainer>
{
    private RabbitMqBroker(Action<RabbitMqBuilder>? configure)
        : base(
            () =>
            {
                var builder = new RabbitMqBuilder("rabbitmq:3");
                configure?.Invoke(builder);
                return builder.Build();
            },
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            container => container.GetConnectionString())
    {
    }

    public override string Id => "broker:rabbitmq";

    public override string Kind => "broker";

    public override string Description => "RabbitMQ container";

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public static RabbitMqBroker Container(Action<RabbitMqBuilder>? configure = null)
        => new(configure);

    /// <summary>Starts a container now, or throws with the reason it could not start.</summary>
    public static RabbitMqBroker Start(Action<RabbitMqBuilder>? configure = null)
    {
        var result = TryStart(configure);
        return result.Resource
            ?? throw new InvalidOperationException($"The RabbitMQ container did not start: {result.Error}");
    }

    /// <summary>
    /// Starts a container, reporting why it could not start instead of throwing - a machine without a
    /// container runtime should be able to fall back or skip rather than fail the run.
    /// </summary>
    public static ContainerStartResult<RabbitMqBroker> TryStart(Action<RabbitMqBuilder>? configure = null)
        => TryStartContainer(Container(configure));
}
