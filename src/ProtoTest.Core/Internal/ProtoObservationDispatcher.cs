namespace ProtoTest.Core.Internal;

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

internal sealed class ProtoObservationDispatcher(IServiceProvider services)
{
    private readonly ConcurrentBag<ProtoObservation> _observations = [];

    public IReadOnlyCollection<ProtoObservation> Snapshot() => [.. _observations];

    /// <summary>
    /// Stores the observation, then offers it to every collector. A collector that throws cannot fail
    /// the test or starve the collectors behind it: the failures are returned so the caller can trace
    /// them, matching how sinks are isolated.
    /// </summary>
    public IReadOnlyList<Exception> Record(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _observations.Add(observation);

        List<Exception>? failures = null;
        foreach (var collector in services.GetServices<IProtoCollector>())
        {
            try
            {
                if (collector.CanCollect(observation))
                {
                    collector.Collect(observation);
                }
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        return failures ?? [];
    }
}
