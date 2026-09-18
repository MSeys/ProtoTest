namespace ProtoTest.Web.Playwright;

using Microsoft.Playwright;

/// <summary>
/// Test-scoped browser pool. Browser processes are shared by the sessions of one test, each receiving
/// its own isolated browser context, and are disposed with the test. Scoping per test contains
/// browser-level failures and cleans up any native contexts a test opens directly.
/// </summary>
internal sealed class PlaywrightBrowserPool : IAsyncDisposable
{
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly HashSet<string> InstalledBrowsers = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<BrowserKey, IBrowser> _browsers = [];
    private IPlaywright? _playwright;
    private int _disposeStarted;

    public async ValueTask<IBrowser> GetBrowserAsync(
        PlaywrightWebOptions options,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeStarted) != 0, this);
            var key = new BrowserKey(options.Browser, options.Headless, options.SlowMo, options.Channel);
            if (_browsers.TryGetValue(key, out var existing)) return existing;

            await EnsureBrowserInstalledAsync(options);
            _playwright ??= await Microsoft.Playwright.Playwright.CreateAsync();
            var browserType = options.Browser switch
            {
                PlaywrightBrowser.Chromium => _playwright.Chromium,
                PlaywrightBrowser.Firefox => _playwright.Firefox,
                PlaywrightBrowser.Webkit => _playwright.Webkit,
                _ => throw new ArgumentOutOfRangeException(nameof(options.Browser))
            };
            var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
                SlowMo = options.SlowMo,
                Channel = options.Channel
            });
            _browsers.Add(key, browser);
            return browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        Exception? failure = null;
        foreach (var browser in _browsers.Values.Reverse())
        {
            try { await browser.DisposeAsync(); }
            catch (Exception exception) { failure ??= exception; }
        }
        try { _playwright?.Dispose(); }
        catch (Exception exception) { failure ??= exception; }
        _gate.Dispose();
        if (failure is not null) throw failure;
    }

    private static async Task EnsureBrowserInstalledAsync(PlaywrightWebOptions options)
    {
        if (!options.InstallBrowsers || !string.IsNullOrWhiteSpace(options.Channel))
        {
            return;
        }

        var browser = options.Browser switch
        {
            PlaywrightBrowser.Firefox => "firefox",
            PlaywrightBrowser.Webkit => "webkit",
            _ => "chromium"
        };
        if (InstalledBrowsers.Contains(browser))
        {
            return;
        }

        await InstallGate.WaitAsync();
        try
        {
            if (InstalledBrowsers.Contains(browser))
            {
                return;
            }

            // The driver ships with the package and downloads only what it is missing; installing a
            // present browser is a cheap no-op.
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(["install", browser]));
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Installing the Playwright '{browser}' browser failed with exit code {exitCode}. " +
                    $"Run 'playwright.ps1 install {browser}' once, or set " +
                    "'ProtoTest:Web:Playwright:InstallBrowsers=false' and provide the browser yourself.");
            }

            InstalledBrowsers.Add(browser);
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private sealed record BrowserKey(
        PlaywrightBrowser Browser,
        bool Headless,
        float? SlowMo,
        string? Channel);
}
