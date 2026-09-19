namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;
using Microsoft.Playwright;

/// <summary>
/// The browser-availability probe behind <see cref="RequiresPlaywrightBrowserAttribute"/>. It never
/// launches a browser: a bundled browser is checked through <see cref="IBrowserType.ExecutablePath"/>,
/// and a channel is checked by asking the Playwright driver itself (a dry run, no download or install).
/// </summary>
internal static class PlaywrightBrowserProbe
{
    /// <summary>Marks a probed (browser, channel) combination as able to run.</summary>
    private const string Available = "";

    private static readonly ConcurrentDictionary<string, string> Probed = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns null when the browser can run, otherwise the reason it cannot. Only success is cached
    /// (keyed on the resolved browser and channel), so an install after a miss is observed and different
    /// host options never reuse another combination's verdict. <c>InstallBrowsers=true</c> is never
    /// cached or probed: installing is the caller's answer, so it always reports availability.
    /// </summary>
    internal static string? GetSkipReason(PlaywrightWebOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.InstallBrowsers)
        {
            return null;
        }

        var key = $"{options.Browser}|{options.Channel}";
        if (Probed.TryGetValue(key, out var cached))
        {
            return cached.Length == 0 ? null : cached;
        }

        var reason = Probe(options);
        if (reason is null)
        {
            Probed[key] = Available;
        }

        return reason;
    }

    /// <summary>The executable path the driver reports for a bundled browser, or null when it cannot be read.</summary>
    internal static string? ExecutablePath(PlaywrightBrowser browser)
    {
        try
        {
            return FindExecutablePath(browser);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Whether the driver recognizes a system browser channel (a dry run, no download).</summary>
    internal static bool ChannelIsKnown(string channel)
    {
        try
        {
            return ProbeChannel(channel) is null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Produces the missing-browser reason directly, for tests and for callers that already resolved an
    /// executable path. Returns null when the path exists.
    /// </summary>
    internal static string? MissingBrowserReason(PlaywrightBrowser browser, string? executablePath, string? failure = null)
    {
        if (failure is null && executablePath is not null && File.Exists(executablePath))
        {
            return null;
        }

        var name = BrowserName(browser);
        var detail = failure is not null
            ? $"Playwright could not check for it: {failure}"
            : executablePath is null
                ? "Playwright did not report an executable path."
                : $"Expected '{executablePath}'.";
        return $"The Playwright {name} browser is not installed. {detail} " +
               $"Run 'playwright.ps1 install {name}', set 'ProtoTest:Web:Playwright:InstallBrowsers=true', " +
               "or configure a channel that names an installed system browser.";
    }

    private static string? Probe(PlaywrightWebOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Channel))
        {
            return ProbeChannel(options.Channel);
        }

        try
        {
            // The contract is synchronous, so block on a pool thread to stay clear of any runner sync context.
            var executable = Task.Run(() => FindExecutablePath(options.Browser)).GetAwaiter().GetResult();
            return MissingBrowserReason(options.Browser, executable);
        }
        catch (Exception exception)
        {
            return MissingBrowserReason(options.Browser, executablePath: null, failure: exception.Message);
        }
    }

    private static string? FindExecutablePath(PlaywrightBrowser browser)
    {
        // Starting the driver is enough; no browser process is launched.
        using var playwright = Microsoft.Playwright.Playwright.CreateAsync().GetAwaiter().GetResult();
        return BrowserType(playwright, browser).ExecutablePath;
    }

    private static string? ProbeChannel(string channel)
    {
        // The driver's own validation: a dry run prints information without downloading or writing anything.
        var result = new Microsoft.Playwright.Program().RunWithResult(["install", "--dry-run", "--no-progress", channel]);
        if (result.ExitCode == 0)
        {
            return null;
        }

        var detail = FirstLine(result.StandardError)
            ?? FirstLine(result.StandardOutput)
            ?? $"the Playwright driver exited with code {result.ExitCode}.";
        return $"Playwright does not recognize the browser channel '{channel}': {detail}";
    }

    private static IBrowserType BrowserType(IPlaywright playwright, PlaywrightBrowser browser) => browser switch
    {
        PlaywrightBrowser.Chromium => playwright.Chromium,
        PlaywrightBrowser.Firefox => playwright.Firefox,
        PlaywrightBrowser.Webkit => playwright.Webkit,
        _ => throw new ArgumentOutOfRangeException(nameof(browser))
    };

    private static string BrowserName(PlaywrightBrowser browser) => browser switch
    {
        PlaywrightBrowser.Chromium => "chromium",
        PlaywrightBrowser.Firefox => "firefox",
        PlaywrightBrowser.Webkit => "webkit",
        _ => throw new ArgumentOutOfRangeException(nameof(browser))
    };

    private static string? FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var line = value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }
}
