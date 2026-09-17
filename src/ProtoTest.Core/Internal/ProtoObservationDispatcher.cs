namespace ProtoTest.Core.Internal;

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

internal sealed class ProtoObservationDispatcher(IServiceProvider services)
{
    private readonly ConcurrentBag<ProtoObservation> _observations = [];

    public IReadOnlyCollection<ProtoObservation> Snapshot() => [.. _observations];

    public void Record(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _observations.Add(observation);

        foreach (var collector in services.GetServices<IProtoCollector>())
        {
            if (collector.CanCollect(observation))
            {
                collector.Collect(observation);
            }
        }
    }
}
