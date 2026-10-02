namespace ProtoTest.Web.Tests;

using Microsoft.Playwright;

/// <summary>Helpers for the tests that drive the Playwright page directly.</summary>
internal static class PlaywrightPageExtensions
{
    // The backend gives every page call the suite's action timeout, and several tests shorten it so a
    // failure arrives quickly. Loading the test's own markup is setup, not behaviour under test, and the
    // first page of a cold runner can take several seconds, so it gets a budget of its own.
    private static readonly PageSetContentOptions Setup = new() { Timeout = 60_000 };

    /// <summary>Loads the markup a test runs against, outside the action timeout the test is checking.</summary>
    public static Task LoadMarkupAsync(this IPage page, string html) => page.SetContentAsync(html, Setup);
}
