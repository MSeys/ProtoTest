namespace ProtoTest.Web.Selenium;

using System.Diagnostics;
using OpenQA.Selenium;
using ProtoTest.Web.Internal;

public sealed partial class SeleniumWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics
{
    private async ValueTask ExecuteActionableAsync(
        WebElementReference reference,
        WebOperationKind operation,
        Action<IWebElement> action,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var attempt = 0;
        ElementBounds? previousBounds = null;
        var clickLike = operation is WebOperationKind.Click or WebOperationKind.Check;
        var editable = operation is WebOperationKind.Fill or WebOperationKind.SelectOption;

        WebProbe Attempt()
        {
            attempt++;
            try
            {
                var scope = ResolveScope(reference);
                var element = ResolveSingle(scope, reference.Locator, reference.ComponentPath);
                if (!element.Displayed || !element.Enabled)
                {
                    previousBounds = null;
                    return Waiting(
                        $"displayed={element.Displayed.ToString().ToLowerInvariant()}, enabled={element.Enabled.ToString().ToLowerInvariant()}");
                }

                if (editable && IsReadOnly(element))
                {
                    return Waiting("element is readonly");
                }

                if (clickLike)
                    ScrollIntoView(element);

                if (clickLike && _options.WaitForStableBounds)
                {
                    var bounds = new ElementBounds(
                        element.Location.X,
                        element.Location.Y,
                        element.Size.Width,
                        element.Size.Height);
                    if (previousBounds is null || previousBounds.Value != bounds)
                    {
                        previousBounds = bounds;
                        return Waiting("element bounds are not stable yet");
                    }
                }

                if (clickLike && _options.CheckClickObstruction && IsObstructed(element))
                {
                    return Waiting("another element covers the click point");
                }

                action(element);
                Record(operation, reference, attempt, "succeeded", "action completed", stopwatch.Elapsed);
                return new WebProbe(true, "action completed");
            }
            catch (NoSuchElementException) { return Retry("not found"); }
            catch (StaleElementReferenceException)
            {
                previousBounds = null;
                return Retry("stale element; resolving again");
            }
            catch (ElementClickInterceptedException exception) { return Retry($"click intercepted: {exception.Message}"); }
            catch (ElementNotInteractableException exception) { return Retry($"not interactable: {exception.Message}"); }
            catch (InvalidElementStateException exception) { return Retry($"invalid element state: {exception.Message}"); }
        }

        WebProbe Waiting(string observation)
        {
            Record(operation, reference, attempt, "waiting", observation, stopwatch.Elapsed);
            return new WebProbe(false, observation);
        }

        WebProbe Retry(string observation)
        {
            Record(operation, reference, attempt, "retry", observation, stopwatch.Elapsed);
            return new WebProbe(false, observation);
        }

        var result = await _probes.PollAsync(
            token => _executor.RunAsync(Attempt, token),
            observation => observation.Holds,
            _options.ActionTimeout,
            cancellationToken);
        if (!result.Satisfied)
        {
            Record(operation, reference, attempt, "failed", result.Value.Observation, stopwatch.Elapsed);
            throw WebBackendErrors.NotActionable(reference, _options.ActionTimeout, result.Value.Observation);
        }
    }

    private async ValueTask<IWebElement> ResolvePresentAsync(
        WebElementReference reference,
        CancellationToken cancellationToken)
    {
        var result = await _probes.PollAsync(
            token => _executor.RunAsync(() => TryResolve(reference), token),
            element => element is not null,
            _options.ActionTimeout,
            cancellationToken);
        return result.Value ?? throw WebBackendErrors.NotPresent(reference, _options.ActionTimeout);
    }

    private IWebElement? TryResolve(WebElementReference reference)
    {
        try
        {
            return ResolveSingle(ResolveScope(reference), reference.Locator, reference.ComponentPath);
        }
        catch (NoSuchElementException) { return null; }
        catch (StaleElementReferenceException) { return null; }
    }

    private ISearchContext ResolveScope(WebElementReference reference)
    {
        ISearchContext scope = Driver;
        foreach (var root in reference.ComponentRoots)
            scope = ResolveSingle(scope, root, reference.ComponentPath);
        return scope;
    }

    private static IWebElement ResolveSingle(ISearchContext scope, WebLocator locator, string componentPath)
    {
        var matches = ResolveMany(scope, locator, componentPath);
        return matches.Count switch
        {
            0 => throw new NoSuchElementException($"No element matched {locator.Describe()} in {componentPath}."),
            1 => matches[0],
            _ => throw WebBackendErrors.MultipleMatch(locator, componentPath, matches.Count)
        };
    }

    /// <summary>
    /// Resolves a locator to every element it matches. An <c>At(index)</c> is applied to the matches of
    /// its source, recursively, so nesting addresses a position within the source's matches rather than
    /// silently dropping the inner index.
    /// </summary>
    private static IReadOnlyList<IWebElement> ResolveMany(
        ISearchContext scope,
        WebLocator locator,
        string componentPath,
        bool throwOnMissing = true)
    {
        if (locator is NthWebLocator nth)
        {
            var source = ResolveMany(scope, nth.Source, componentPath, throwOnMissing);
            if (source.Count <= nth.Index)
            {
                if (!throwOnMissing) return [];
                throw new NoSuchElementException(
                    $"No element exists at zero-based index {nth.Index} for {nth.Source.Describe()} in {componentPath}; found {source.Count}.");
            }

            return [source[nth.Index]];
        }

        return scope.FindElements(SeleniumLocatorTranslator.Translate(locator, scope is IWebDriver));
    }

    private static bool IsReadOnly(IWebElement element)
        => element.GetDomAttribute("readonly") is not null ||
           string.Equals(element.GetDomAttribute("aria-readonly"), "true", StringComparison.OrdinalIgnoreCase);

    private bool TryInspect(WebElementReference reference, Func<IWebElement, bool> inspect)
    {
        try
        {
            return inspect(ResolveSingle(ResolveScope(reference), reference.Locator, reference.ComponentPath));
        }
        catch (NoSuchElementException) { return false; }
        catch (StaleElementReferenceException) { return false; }
    }

    private bool IsObstructed(IWebElement element)
    {
        if (Driver is not IJavaScriptExecutor javascript) return false;
        try
        {
            const string script = "var e=arguments[0],r=e.getBoundingClientRect()," +
                                  "x=r.left+r.width/2,y=r.top+r.height/2,h=document.elementFromPoint(x,y);" +
                                  "return h!==e&&!e.contains(h);";
            return Convert.ToBoolean(javascript.ExecuteScript(script, element));
        }
        catch (WebDriverException)
        {
            // Older or restricted drivers may not support this diagnostic primitive.
            return false;
        }
    }

    private void ScrollIntoView(IWebElement element)
    {
        if (Driver is not IJavaScriptExecutor javascript) return;
        try
        {
            javascript.ExecuteScript(
                "arguments[0].scrollIntoView({block:'center',inline:'center',behavior:'instant'});",
                element);
        }
        catch (WebDriverException)
        {
            // Selenium's native click still gets a chance to scroll on older drivers.
        }
    }

    private readonly record struct ElementBounds(int X, int Y, int Width, int Height);

    /// <summary>The native value Selenium sends for each semantic key.</summary>
    internal static readonly IReadOnlyDictionary<WebKey, string> KeyMap = new Dictionary<WebKey, string>
    {
        [WebKey.Enter] = OpenQA.Selenium.Keys.Enter,
        [WebKey.Tab] = OpenQA.Selenium.Keys.Tab,
        [WebKey.Escape] = OpenQA.Selenium.Keys.Escape,
        [WebKey.Space] = OpenQA.Selenium.Keys.Space,
        [WebKey.Backspace] = OpenQA.Selenium.Keys.Backspace,
        [WebKey.Delete] = OpenQA.Selenium.Keys.Delete,
        [WebKey.ArrowUp] = OpenQA.Selenium.Keys.ArrowUp,
        [WebKey.ArrowDown] = OpenQA.Selenium.Keys.ArrowDown,
        [WebKey.ArrowLeft] = OpenQA.Selenium.Keys.ArrowLeft,
        [WebKey.ArrowRight] = OpenQA.Selenium.Keys.ArrowRight,
        [WebKey.Home] = OpenQA.Selenium.Keys.Home,
        [WebKey.End] = OpenQA.Selenium.Keys.End,
        [WebKey.PageUp] = OpenQA.Selenium.Keys.PageUp,
        [WebKey.PageDown] = OpenQA.Selenium.Keys.PageDown
    };

    private static string MapKey(WebKey key)
        => KeyMap.TryGetValue(key, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(key));
}
