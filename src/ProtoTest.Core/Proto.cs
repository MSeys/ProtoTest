namespace ProtoTest.Core;

/// <summary>
/// Provides a static gateway to the active <see cref="ProtoExecutionContext"/>.
/// </summary>
public static class Proto
{
    /// <summary>
    /// Gets the active ProtoTest host.
    /// </summary>
    public static ProtoHost Host => ProtoHost.CurrentHost;

    /// <summary>
    /// Gets the current active test execution context.
    /// </summary>
    /// <remarks>
    /// This is the intended lookup for everything that runs inside a test on the test's flow:
    /// test bodies, test-author entries and the plumbing they call. It throws when no test is active,
    /// so code that runs off the test's flow (telemetry callbacks, library threads) must use
    /// <see cref="ProtoHost.FindTraceWriter(System.Diagnostics.Activity?)"/> and correlate by trace id
    /// instead, and run-level code must use its host reference or <see cref="ProtoHost.CurrentHost"/>.
    /// See the "Context lookups" section in CONTRIBUTING.md.
    /// </remarks>
    public static ProtoExecutionContext Context => ProtoHost.CurrentContext;
}
