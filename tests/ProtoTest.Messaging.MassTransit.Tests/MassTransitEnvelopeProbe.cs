namespace ProtoTest.Messaging.MassTransit.Tests;

using System.Globalization;
using global::MassTransit;

/// <summary>A command the suite wraps and publishes for the bus's consumer; the run id isolates parallel tests.</summary>
public sealed record EnvelopeProbeCommand(Guid RunId, int InvoiceId, decimal Amount);

/// <summary>The event the bus publishes for the suite to await; the run id isolates parallel tests.</summary>
public sealed record EnvelopeProbeEvent(Guid RunId, int InvoiceId, decimal Amount);

/// <summary>What a bus consumer received, with the envelope metadata the suite asserts on.</summary>
internal sealed record ConsumedEnvelope(EnvelopeProbeCommand Message, Guid? CorrelationId, string? Tenant);

/// <summary>Receives the suite's command on the bus and records the message the envelope carried.</summary>
internal sealed class EnvelopeProbeCommandConsumer(Guid runId, TaskCompletionSource<ConsumedEnvelope> received)
    : IConsumer<EnvelopeProbeCommand>
{
    public Task Consume(ConsumeContext<EnvelopeProbeCommand> context)
    {
        if (context.Message.RunId == runId)
        {
            var tenant = context.Headers.TryGetHeader("tenant", out var value)
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : null;
            received.TrySetResult(new ConsumedEnvelope(context.Message, context.CorrelationId, tenant));
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Consumes the event the bus publishes, so the test bus's topology declares the event's exchange -
/// the same reason an application consumes what it publishes.
/// </summary>
internal sealed class EnvelopeProbeEventConsumer : IConsumer<EnvelopeProbeEvent>
{
    public Task Consume(ConsumeContext<EnvelopeProbeEvent> context) => Task.CompletedTask;
}
