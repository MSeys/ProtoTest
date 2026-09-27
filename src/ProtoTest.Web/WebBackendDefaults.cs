namespace ProtoTest.Web;

/// <summary>
/// The web layer's timing defaults: how long a wait or bounded assertion runs before failing, and how
/// often it probes. Both backends and a hand-written one read the timeout default for their options, so
/// every backend fails at the same speed unless a test configures otherwise; the poll default is what
/// <see cref="IWebBackend.PollInterval"/> returns for a backend without an interval option.
/// </summary>
public static class WebBackendDefaults
{
    /// <summary>The default timeout for a wait condition or a bounded element assertion.</summary>
    public static TimeSpan DefaultTimeout => Internal.WebTiming.DefaultTimeout;

    /// <summary>The default interval between probes.</summary>
    public static TimeSpan DefaultPollInterval => Internal.WebTiming.DefaultPollInterval;
}
