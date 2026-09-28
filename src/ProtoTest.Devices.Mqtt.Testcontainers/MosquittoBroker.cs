namespace ProtoTest.Devices.Mqtt.Testcontainers;

// global:: because this assembly's own namespace ends in Testcontainers.
using global::Testcontainers.Mosquitto;
using ProtoTest.Testcontainers;

/// <summary>
/// A Mosquitto container owned by the whole run: started once for the suite and released when the host
/// is disposed, after the run has stopped and the reports are written. Register it as a
/// <c>UseContainer(...)</c> provider of the target that declares
/// <c>MqttDeviceOptions.BrokerSetting</c>, so the broker address reaches the MQTT clients through the
/// one settings key - the same shape as the RabbitMQ and PostgreSQL containers.
/// </summary>
public sealed class MosquittoBroker : ProtoContainerResource<MosquittoContainer>
{
    /// <summary>The MQTT listener's port in the image.</summary>
    private const int MqttPort = 1883;

    private MosquittoBroker(Action<MosquittoBuilder>? configure)
        : base(
            () =>
            {
                var builder = new MosquittoBuilder("eclipse-mosquitto:2.0");
                configure?.Invoke(builder);
                return builder.Build();
            },
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            container => container.GetConnectionString())
    {
        // The image's own wait strategy reads the log; this check is the run's own, named and traced,
        // and waits for the MQTT port to accept a connection.
        ReadyWhenTcp(
            "Mosquitto accepts MQTT connections",
            defaultPort: MqttPort,
            (container, port) => (container.Hostname, container.GetMappedPublicPort(port)));
    }

    public override string Id => "broker:mqtt";

    public override string Kind => "broker";

    public override string Description => "Mosquitto MQTT broker container";

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public static MosquittoBroker Container(Action<MosquittoBuilder>? configure = null)
        => new(configure);

    /// <summary>Starts a container now, or throws with the reason it could not start.</summary>
    public static MosquittoBroker Start(Action<MosquittoBuilder>? configure = null)
    {
        var result = TryStart(configure);
        return result.Resource
            ?? throw new InvalidOperationException($"The Mosquitto container did not start: {result.Error}");
    }

    /// <summary>
    /// Starts a container, reporting why it could not start instead of throwing - a machine without a
    /// container runtime should be able to fall back or skip rather than fail the run.
    /// </summary>
    public static ContainerStartResult<MosquittoBroker> TryStart(Action<MosquittoBuilder>? configure = null)
        => TryStartContainer(Container(configure));
}
