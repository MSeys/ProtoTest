namespace ProtoTest.Core.Internal;

/// <summary>
/// The <see cref="TimeProvider"/> an in-process application or worker receives. A request carrying a
/// test's clock (pushed by the hosting integration) sees that clock; a flow with an active test sees
/// the test's clock; a background flow with neither (a worker's loop, run setup) sees the run clock.
/// This is what makes <c>Proto.Context.Clock().Advance(...)</c> visible to the application under test
/// without the application knowing about ProtoTest.
/// </summary>
internal sealed class ProtoTestTimeProvider(ProtoClock runClock) : TimeProvider
{
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

    public override DateTimeOffset GetUtcNow()
        => ProtoRequestClock.Current?.GetUtcNow()
            ?? ProtoTestLifecycle.TryGetCurrentContext?.Clock.GetUtcNow()
            ?? runClock.GetUtcNow();
}
