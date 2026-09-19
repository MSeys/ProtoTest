namespace ProtoTest.Web;

/// <summary>
/// The assertions for one element, in their positive (<see cref="WebElement.Should"/>) or negated
/// (<see cref="WebElement.ShouldNot"/>) form. Each assertion polls until it passes or its timeout
/// elapses.
/// </summary>
public sealed class WebAssertions
{
    private readonly WebElement _element;
    private readonly bool _negated;

    internal WebAssertions(WebElement element, bool negated)
    {
        _element = element;
        _negated = negated;
    }

    /// <summary>Asserts that the element becomes visible.</summary>
    public ValueTask BeVisibleAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldBeVisibleAsync(_element.Reference, _negated, timeout, cancellationToken);

    /// <summary>Asserts that the element becomes enabled.</summary>
    public ValueTask BeEnabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldBeEnabledAsync(_element.Reference, _negated, timeout, cancellationToken);

    /// <summary>Asserts that the element becomes checked.</summary>
    public ValueTask BeCheckedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldBeCheckedAsync(_element.Reference, _negated, timeout, cancellationToken);

    /// <summary>Asserts that the element's text becomes exactly <paramref name="expected"/>.</summary>
    public ValueTask HaveTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldHaveTextAsync(_element.Reference, expected, contains: false, _negated, timeout, cancellationToken);

    /// <summary>Asserts that the element's text becomes a superstring of <paramref name="expected"/>.</summary>
    public ValueTask ContainTextAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldHaveTextAsync(_element.Reference, expected, contains: true, _negated, timeout, cancellationToken);

    /// <summary>Asserts that the element's value becomes exactly <paramref name="expected"/>.</summary>
    public ValueTask HaveValueAsync(string expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => _element.Session.ShouldHaveValueAsync(_element.Reference, expected, _negated, timeout, cancellationToken);
}
