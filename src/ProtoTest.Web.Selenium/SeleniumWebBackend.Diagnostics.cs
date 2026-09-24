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
        try { await _executor.RunAsync(() => Driver.Quit(), CancellationToken.None); }
        catch (Exception exception) { failure = exception; }
        try { await _executor.RunAsync(() => Driver.Dispose(), CancellationToken.None); }
        catch (Exception exception) { failure ??= exception; }
        await _executor.DisposeAsync();
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
