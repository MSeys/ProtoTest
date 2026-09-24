namespace ProtoTest.Web;

/// <summary>
/// Finds the page routes a frontend source folder defines, so pages that exist in the source show as
/// uncovered until a browser test verifies them. A missing, unreadable or malformed folder yields no
/// routes: a framework that isn't there is an answer, not a failure.
/// </summary>
public interface IWebPageSourceScanner
{
    /// <summary>Discovers the normalized route patterns the source folder defines.</summary>
    IReadOnlyList<string> Discover(WebPageSourceOptions options);
}
