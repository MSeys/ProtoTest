namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Configures the run's clock.</summary>
public static class ProtoClockExtensions
{
    /// <summary>
    /// Seeds the run's clock. Every test's clock starts where this one points, so a fixed instant makes
    /// time-dependent tests deterministic; without this call the run starts at the current real time.
    /// Register it while composing the host, before the run starts.
    /// </summary>
    public static IProtoHostBuilder ConfigureClock(this IProtoHostBuilder builder, ProtoClock clock)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(clock);
        return builder.ConfigureServices(services => services.AddSingleton(clock));
    }
}
