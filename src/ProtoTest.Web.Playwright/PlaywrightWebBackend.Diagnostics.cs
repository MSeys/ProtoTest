namespace ProtoTest.Web.Playwright;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public sealed partial class PlaywrightWebBackend : IWebBackend, IWebBackendJavaScript, IWebBackendDiagnostics, IWebBackendDownloads
{
    private void WireDiagnostics()
    {
        if (_options.ConsoleCapture != PlaywrightConsoleCapture.Off)
        {
            Page.Console += (_, message) =>
            {
                if (!ShouldCaptureConsole(message.Type)) return;
                _context.Trace.WriteEvent(
                    "web.browser.console",
                    $"Browser console · {message.Type}",
                    TraceSource,
                    outcome: ProtoTraceOutcome.Unknown,
                    attributes: new Dictionary<string, string?>
                    {
                        ["web.session"] = _sessionName,
                        ["web.correlation_id"] = _correlation.Latest,
                        ["browser.console.type"] = message.Type,
                        ["browser.console.text"] = Truncate(message.Text)
                    },
                    parentId: _correlation.Latest);
            };
        }

        if (_options.CapturePageErrors)
        {
            Page.PageError += (_, message) => _context.Trace.WriteEvent(
                "web.browser.page_error",
                "Browser page error",
                TraceSource,
                outcome: ProtoTraceOutcome.Unknown,
                attributes: new Dictionary<string, string?>
                {
                    ["web.session"] = _sessionName,
                    ["web.correlation_id"] = _correlation.Latest,
                    ["browser.error.message"] = Truncate(message)
                },
                parentId: _correlation.Latest);
        }

        if (_options.CaptureRequestFailures)
        {
            Page.RequestFailed += (_, request) => _context.Trace.WriteEvent(
                "web.browser.request_failed",
                $"Request failed · {request.Method}",
                TraceSource,
                outcome: ProtoTraceOutcome.Unknown,
                attributes: new Dictionary<string, string?>
                {
                    ["web.session"] = _sessionName,
                    ["web.correlation_id"] = _correlation.Latest,
                    ["http.method"] = request.Method,
                    ["http.url"] = SafeUrl(request.Url),
                    ["browser.request.failure"] = Truncate(request.Failure)
                },
                parentId: _correlation.Latest);
        }
    }

    private bool ShouldCaptureConsole(string type)
        => _options.ConsoleCapture switch
        {
            PlaywrightConsoleCapture.All => true,
            PlaywrightConsoleCapture.Errors => string.Equals(type, "error", StringComparison.OrdinalIgnoreCase),
            PlaywrightConsoleCapture.WarningsAndErrors =>
                string.Equals(type, "error", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "warning", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "warn", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

    private void TraceDiagnosticFailure(string stage, Exception exception, string correlationId)
        => _context.Trace.WriteEvent(
            "web.playwright.correlation_failed",
            $"Playwright trace correlation failed · {stage}",
            TraceSource,
            outcome: ProtoTraceOutcome.Unknown,
            attributes: new Dictionary<string, string?>
            {
                ["web.session"] = _sessionName,
                ["web.correlation_id"] = correlationId,
                ["web.diagnostics.stage"] = stage,
                ["web.diagnostics.error"] = exception.Message
            },
            parentId: correlationId);

    internal static string? SafeUrl(string? value) => ProtoUriSanitizer.ForDiagnostics(value);

    private static string? Truncate(string? value)
        => value is null || value.Length <= 4096 ? value : value[..4096] + "…";

}
