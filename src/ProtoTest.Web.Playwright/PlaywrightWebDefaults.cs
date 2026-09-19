namespace ProtoTest.Web.Playwright;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

/// <summary>
/// The code-configured launch options a Playwright registration exposes to the browser probe, so a skip
/// condition sees the same defaults the backend would launch with. The host overload contributes them as
/// an in-memory configuration source; the application overload cannot reach the host's configuration
/// sources, so it registers the same values here and the probe merges them under the host's configuration.
/// </summary>
internal static class PlaywrightWebDefaults
{
    private static readonly ConditionalWeakTable<IConfiguration, DefaultsSnapshot> ApplicationDefaults = new();

    /// <summary>Creates the default options of one registration by running its configure callback once.</summary>
    internal static PlaywrightWebOptions Create(Action<PlaywrightWebOptions>? configure)
    {
        var defaults = new PlaywrightWebOptions();
        configure?.Invoke(defaults);
        return defaults;
    }

    /// <summary>The defaults as an in-memory configuration source, for the host overload to insert first.</summary>
    internal static MemoryConfigurationSource Source(PlaywrightWebOptions defaults) => new()
    {
        InitialData = new Dictionary<string, string?>
        {
            ["ProtoTest:Web:Playwright:Browser"] = defaults.Browser.ToString(),
            ["ProtoTest:Web:Playwright:Channel"] = defaults.Channel,
            ["ProtoTest:Web:Playwright:InstallBrowsers"] = defaults.InstallBrowsers.ToString()
        }
    };

    /// <summary>Makes an application's code-configured defaults visible to the probe for one host.</summary>
    internal static void Register(IConfiguration configuration, PlaywrightWebOptions defaults)
        => ApplicationDefaults.AddOrUpdate(
            configuration,
            new DefaultsSnapshot(defaults.Browser, defaults.Channel, defaults.InstallBrowsers));

    /// <summary>
    /// Resolves the probe's options: application code defaults first, then the host's
    /// <c>ProtoTest:Web:Playwright</c> section, then - when a session is named - that session's
    /// <c>ProtoTest:Web:Sessions:{name}</c> section, mirroring the order the backend binds.
    /// </summary>
    internal static PlaywrightWebOptions Resolve(IConfiguration configuration, string? sessionName = null)
    {
        var options = new PlaywrightWebOptions();
        if (ApplicationDefaults.TryGetValue(configuration, out var defaults))
        {
            options.Browser = defaults.Browser;
            options.Channel = defaults.Channel;
            options.InstallBrowsers = defaults.InstallBrowsers;
        }

        Apply(options, Section(configuration, PlaywrightWebOptions.ConfigurationSectionName));
        if (!string.IsNullOrWhiteSpace(sessionName))
        {
            Apply(options, Section(configuration, $"ProtoTest:Web:Sessions:{sessionName}"));
        }

        return options;
    }

    private static IReadOnlyDictionary<string, string?> Section(IConfiguration configuration, string key)
        => configuration.GetSection(key).GetChildren().ToDictionary(child => child.Key, child => child.Value);

    private static void Apply(PlaywrightWebOptions options, IReadOnlyDictionary<string, string?> values)
    {
        if (values.TryGetValue("Browser", out var browser)
            && Enum.TryParse<PlaywrightBrowser>(browser, ignoreCase: true, out var parsedBrowser))
        {
            options.Browser = parsedBrowser;
        }

        if (values.ContainsKey("Channel"))
        {
            options.Channel = values["Channel"];
        }

        if (values.TryGetValue("InstallBrowsers", out var installBrowsers)
            && bool.TryParse(installBrowsers, out var parsedInstallBrowsers))
        {
            options.InstallBrowsers = parsedInstallBrowsers;
        }
    }

    private sealed record DefaultsSnapshot(PlaywrightBrowser Browser, string? Channel, bool InstallBrowsers);
}
