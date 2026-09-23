namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// The polling engine behind element assertions and <c>WaitUntilAsync</c>: it retries until a condition
/// holds or the timeout passes, treats "not found yet" and "not actionable yet" as "not yet", and
/// records a passing element assertion as page coverage.
/// </summary>
internal sealed class WebAssertionPoller(WebSession session, WebOperationRunner operations)
{
    public ValueTask ShouldBeVisibleAsync(
        WebElementReference element,
        bool negated,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            negated,
            "be visible",
            timeout,
            async (backend, ct) => await backend.IsVisibleAsync(element, ct)
                ? (true, "visible")
                : (false, "not visible"),
            cancellationToken);

    public ValueTask ShouldBeEnabledAsync(
        WebElementReference element,
        bool negated,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            negated,
            "be enabled",
            timeout,
            async (backend, ct) => await backend.IsEnabledAsync(element, ct)
                ? (true, "enabled")
                : (false, "disabled or absent"),
            cancellationToken);

    public ValueTask ShouldBeCheckedAsync(
        WebElementReference element,
        bool negated,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
        => AssertUntilAsync(
            element,
            negated,
            "be checked",
            timeout,
            async (backend, ct) => await backend.IsCheckedAsync(element, ct)
                ? (true, "checked")
                : (false, "unchecked or absent"),
            cancellationToken);

    public ValueTask ShouldHaveTextAsync(
        WebElementReference element,
        string expected,
        bool contains,
        bool negated,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertUntilAsync(
            element,
            negated,
            contains ? $"contain text \"{expected}\"" : $"have text \"{expected}\"",
            timeout,
            async (backend, ct) =>
            {
                var actual = await backend.ReadTextAsync(element, ct);
                var holds = contains
                    ? actual.Contains(expected, StringComparison.Ordinal)
                    : string.Equals(actual, expected, StringComparison.Ordinal);
                return (holds, $"text was \"{actual}\"");
            },
            cancellationToken);
    }

    public ValueTask ShouldHaveValueAsync(
        WebElementReference element,
        string expected,
        bool negated,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return AssertUntilAsync(
            element,
            negated,
            "have the expected value",
            timeout,
            async (backend, ct) =>
            {
                var actual = await backend.ReadValueAsync(element, ct);
                return (string.Equals(actual, expected, StringComparison.Ordinal),
                    $"value length was {actual?.Length ?? 0} (value redacted)");
            },
            cancellationToken);
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it holds or <paramref name="timeout"/> elapses. Use it to
    /// wait on one session for a change another session produced, or for any condition that is not a
    /// single element assertion.
    /// </summary>
    public ValueTask WaitUntilAsync(
        Func<CancellationToken, ValueTask<bool>> condition,
        TimeSpan? timeout,
        string? description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (waitTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var expectation = string.IsNullOrWhiteSpace(description) ? "the condition to hold" : description;

        return operations.ExecuteVoidAsync(
            "web.wait.until",
            $"Wait · {Shorten(expectation)}",
            WebOperationKind.Assert,
            element: null,
            new Dictionary<string, string?>
            {
                ["web.session"] = session.Name,
                ["web.expectation"] = expectation,
                ["web.wait.timeout"] = waitTimeout.ToString()
            },
            async (_, ct) =>
            {
                var result = await WebPolling.PollAsync(
                    async token =>
                    {
                        try
                        {
                            return await condition(token);
                        }
                        catch (Exception exception) when (
                            exception is WebElementResolutionException or WebActionabilityException)
                        {
                            return false;
                        }
                    },
                    satisfied => satisfied,
                    waitTimeout,
                    WebPolling.DefaultInterval,
                    ct);

                if (!result.Satisfied)
                {
                    throw new WebAssertionException(
                        $"Expected {expectation} within {waitTimeout}, but it did not hold.");
                }
            },
            cancellationToken,
            opensNestingScope: true);
    }

    private async ValueTask AssertUntilAsync(
        WebElementReference element,
        bool negated,
        string expectation,
        TimeSpan? timeout,
        Func<IWebBackend, CancellationToken, ValueTask<(bool Holds, string Observation)>> inspect,
        CancellationToken cancellationToken)
    {
        var assertionTimeout = timeout ?? TimeSpan.FromSeconds(5);
        if (assertionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var describedExpectation = ProtoAssertion.Describe(expectation, negated);
        var attributes = WebSession.ElementAttributes(element);
        attributes["web.expectation"] = describedExpectation;
        attributes["web.assert.negated"] = negated ? "true" : "false";
        attributes["web.assert.timeout"] = assertionTimeout.ToString();
        await operations.ExecuteVoidAsync(
            "assert.web",
            $"Assert · {element.Name} should {describedExpectation}",
            WebOperationKind.Assert,
            element,
            attributes,
            async (backend, ct) =>
            {
                var result = await WebPolling.PollAsync(
                    async token =>
                    {
                        try
                        {
                            return await inspect(backend, token);
                        }
                        catch (Exception exception) when (
                            exception is WebElementResolutionException or WebActionabilityException)
                        {
                            return (Holds: false, Observation: exception.Message);
                        }
                    },
                    observation => ProtoAssertion.IsSatisfied(observation.Holds, negated),
                    assertionTimeout,
                    WebPolling.DefaultInterval,
                    ct);

                if (!result.Satisfied)
                {
                    throw new WebAssertionException(
                        $"Element '{element.ComponentPath}.{element.Name}' should {describedExpectation} within {assertionTimeout}. " +
                        $"Last observed: {result.Value.Observation ?? "no observation"}.");
                }
            },
            cancellationToken);

        // A passing assertion is what makes a page covered; the address is the page it was checked on.
        var backend = await session.GetOrCreateBackendAsync(cancellationToken);
        session.RecordPageObservation(
            "web.page.verified",
            session.PagePathFrom(WebSession.TryCurrentAddress(backend)),
            "assert");
    }

    private static string Shorten(string value)
        => value.Length <= 80 ? value : $"{value[..77]}...";
}
