namespace ProtoTest.Web.Playwright;

using ProtoTest.Core;

/// <summary>
/// Skips the test when the Playwright browser it needs cannot run here. Unlike
/// <see cref="RequiresCapabilityAttribute"/>, which only proves <c>AddWeb</c> was called, this condition
/// probes the installed browser before the lifecycle starts - without launching one.
/// </summary>
/// <remarks>
/// The probe reads the host's Playwright options - the <c>AddWeb(...)</c> callback supplies their defaults,
/// configuration overrides them - so it respects <see cref="PlaywrightWebOptions.InstallBrowsers"/> and
/// <see cref="PlaywrightWebOptions.Channel"/>. A browser that can run produces no skip; a missing one
/// produces a reason naming Playwright and the install options. A channel that Playwright recognizes is
/// accepted as is: channels name a system browser, which only a real launch can resolve, so the condition
/// cannot prove one absent.
/// </remarks>
/// <example>
/// <code>
/// [RequiresPlaywrightBrowser]                                   // the configured browser
/// [RequiresPlaywrightBrowser(browser: PlaywrightBrowser.Firefox)]
/// [RequiresPlaywrightBrowser(channel: "msedge", Reason = "No Edge in this environment.")]
/// [RequiresPlaywrightBrowser(Session = "Admin")]                // the Admin session's options
/// public async Task ...() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequiresPlaywrightBrowserAttribute : ProtoAttribute, IProtoSkipCondition
{
    /// <param name="browser">The browser to require; defaults to the host's configured browser.</param>
    public RequiresPlaywrightBrowserAttribute(PlaywrightBrowser browser)
    {
        Browser = browser;
    }

    /// <param name="channel">
    /// A system browser channel to require (for example <c>msedge</c> or <c>chrome</c>); defaults to the
    /// host's configured channel.
    /// </param>
    public RequiresPlaywrightBrowserAttribute(string channel)
    {
        Channel = channel;
    }

    /// <summary>Requires the host's configured browser.</summary>
    public RequiresPlaywrightBrowserAttribute()
    {
    }

    /// <summary>The required browser, or <see langword="null"/> to use the host's configured browser.</summary>
    public PlaywrightBrowser? Browser { get; }

    /// <summary>The required system browser channel, or <see langword="null"/> to use the host's configuration.</summary>
    public string? Channel { get; }

    /// <summary>
    /// The session whose options the probe follows, or <see langword="null"/> for the protocol-level
    /// section. Set it when the test drives a named session, because the backend binds
    /// <c>ProtoTest:Web:Sessions:{name}</c> over <c>ProtoTest:Web:Playwright</c>; without it a
    /// session-level browser, channel or install-browsers setting could disagree with the launch.
    /// </summary>
    public string? Session { get; init; }

    /// <summary>The reason reported when the browser cannot run.</summary>
    public string? Reason { get; init; }

    public string? GetSkipReason(ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var reason = PlaywrightBrowserProbe.GetSkipReason(Resolve(host));
        return reason is null ? null : Reason ?? reason;
    }

    private PlaywrightWebOptions Resolve(ProtoHost host)
    {
        var options = PlaywrightWebDefaults.Resolve(host.Configuration, Session);
        if (Browser is not null)
        {
            options.Browser = Browser.Value;
        }

        if (!string.IsNullOrWhiteSpace(Channel))
        {
            options.Channel = Channel;
        }

        return options;
    }
}
