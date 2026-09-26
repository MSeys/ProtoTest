namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// The web layer's timing defaults: how long a wait or bounded assertion runs before failing, and how
/// often it probes. Polling, the wait registrations and both backends read these, so every backend
/// fails at the same speed unless a test configures otherwise.
/// </summary>
internal static class WebTiming
{
    /// <summary>The default timeout for a wait condition or a bounded element assertion.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The default interval between probes.</summary>
    public static readonly TimeSpan DefaultPollInterval = ProtoPolling.DefaultInterval;
}
