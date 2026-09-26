namespace ProtoTest.Web.Selenium;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenQA.Selenium;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public sealed partial class SeleniumWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics
{
    private void Record(
        WebOperationKind operation,
        WebElementReference reference,
        int attempt,
        string outcome,
        string observation,
        TimeSpan elapsed)
        => _diagnostics.Enqueue(new SeleniumDiagnosticEntry(
            DateTimeOffset.UtcNow,
            operation.ToString(),
            reference.ComponentPath,
            reference.Name,
            reference.Locator.Describe(),
            attempt,
            outcome,
            observation,
            elapsed.TotalMilliseconds));

    private void RecordCaptureFailure(string artifact, Exception exception)
        => WebFailureArtifacts.TraceArtifactFailure(_context, TraceSource, label: "Selenium", artifact, exception);

    public async ValueTask CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completeStarted, 1) != 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        var retain = _options.DiagnosticTraceRetention == SeleniumDiagnosticTraceRetention.Always ||
                     (_options.DiagnosticTraceRetention == SeleniumDiagnosticTraceRetention.OnWebFailure && _webFailure);
        if (!retain) return;
        try
        {
            string? url = null;
            string? title = null;
            try
            {
                (url, title) = await _executor.RunAsync(() => (Driver.Url, Driver.Title), cancellationToken);
            }
            catch (WebDriverException) { }

            var payload = new
            {
                format = "prototest.selenium.diagnostics.v1",
                startedAtUtc = _startedAtUtc,
                completedAtUtc = DateTimeOffset.UtcNow,
                driverType = Driver.GetType().FullName,
                url,
                title,
                entries = _diagnostics.ToArray()
            };
            _context.AddAttachment(
                $"selenium-{WebNames.SafeName(_sessionName)}-diagnostics.json",
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
                "application/json",
                "Selenium backend diagnostic timeline embedded in ProtoTrace.");
        }
        catch (Exception exception)
        {
            RecordCaptureFailure("selenium-diagnostics", exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;
        Exception? failure = null;
        var quit = _executor.RunAsync(() => Driver.Quit(), CancellationToken.None).AsTask();
        var dispose = _executor.RunAsync(() => Driver.Dispose(), CancellationToken.None).AsTask();
        var abandoned = await _executor.StopAsync();
        if (abandoned is null)
        {
            try { await quit; }
            catch (Exception exception) { failure = exception; }
            try { await dispose; }
            catch (Exception exception) { failure ??= exception; }
        }
        else
        {
            // Release cannot wait forever on a pump stuck in a driver call: report the abandoned work
            // so a hung suite has a trace line naming it, instead of failing disposal for no reason.
            _context.Trace.WriteEvent(
                "web.selenium.executor_abandoned",
                "Selenium driver pump abandoned at release",
                TraceSource,
                ProtoTracePhase.Teardown,
                ProtoTraceOutcome.Failed,
                attributes: new Dictionary<string, string?>
                {
                    ["web.executor"] = nameof(SeleniumDriverExecutor),
                    ["web.executor.work"] = abandoned
                });
        }

        if (failure is not null) throw failure;
    }

    private sealed record SeleniumDiagnosticEntry(
        DateTimeOffset TimestampUtc,
        string Operation,
        string ComponentPath,
        string Element,
        string Locator,
        int Attempt,
        string Outcome,
        string Observation,
        double ElapsedMilliseconds);
}
