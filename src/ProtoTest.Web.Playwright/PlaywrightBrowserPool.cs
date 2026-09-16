namespace ProtoTest.Web.Playwright;

using Microsoft.Playwright;

/// <summary>Test-scoped browser pool; named Web sessions receive isolated contexts on shared browser processes.</summary>
internal sealed class PlaywrightBrowserPool : IAsyncDisposable
{
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

    private sealed record BrowserKey(
        PlaywrightBrowser Browser,
        bool Headless,
        float? SlowMo,
        string? Channel);
}
