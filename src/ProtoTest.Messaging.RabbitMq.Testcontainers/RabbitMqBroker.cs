namespace ProtoTest.Messaging.RabbitMq.Testcontainers;

using ProtoTest.Testcontainers;

// global:: because this assembly's own namespace ends in Testcontainers.
using global::Testcontainers.RabbitMq;

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
        => TryStart(configure, out var broker, out var error)
            ? broker!
            : throw new InvalidOperationException($"The RabbitMQ container did not start: {error}");

    /// <summary>
    /// Starts a container, reporting why it could not start instead of throwing - a machine without a
    /// container runtime should be able to fall back or skip rather than fail the run.
    /// </summary>
    public static bool TryStart(
        Action<RabbitMqBuilder>? configure,
        out RabbitMqBroker? broker,
        out string? error)
    {
        var candidate = Container(configure);
        if (TryStartContainer(candidate, out error))
        {
            broker = candidate;
            return true;
        }

        broker = null;
        return false;
    }
}
