namespace ProtoTest.Web;

/// <summary>
/// The failure messages both backends report for the shared resolution and actionability contract, so
/// the same situation reads the same whichever backend produced it. A hand-written backend composes
/// them so its failures stay indistinguishable from the shipped ones.
/// </summary>
public static class WebBackendErrors
{
    /// <summary>An action that never became actionable, with the backend's last observation when it has one.</summary>
    public static WebActionabilityException NotActionable(
        WebElementReference element,
        TimeSpan timeout,
        string? observation = null)
    {
        var message =
            $"Element '{element.ComponentPath}.{element.Name}' did not become actionable within {timeout}. " +
            $"Locator: {element.Locator.Describe()}.";
        return new WebActionabilityException(
            observation is null ? message : $"{message} Last observed: {observation}.");
    }

    /// <summary>A read of an element that never appeared.</summary>
    public static WebElementResolutionException NotPresent(WebElementReference element, TimeSpan timeout)
        => new(
            $"Element '{element.ComponentPath}.{element.Name}' was not present within {timeout}. " +
            $"Locator: {element.Locator.Describe()}.");

    /// <summary>More than one element where a single-element operation requires at most one.</summary>
    public static WebElementResolutionException MultipleMatch(WebLocator locator, string componentPath, int? count)
        => new(count is { } found
            ? $"Expected at most one element for {locator.Describe()} in {componentPath}, but found {found}."
            : $"Expected at most one element for {locator.Describe()} in {componentPath}, but more than one element matched.");
}
