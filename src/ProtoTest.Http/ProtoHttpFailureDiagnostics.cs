namespace ProtoTest.Http;

/// <summary>
/// The sanitized facts shared by every HTTP-based failure observation, so REST, GraphQL and any future
/// protocol redact and report a failed call the same way.
/// </summary>
public sealed record ProtoHttpFailureDiagnostics(
    string? RequestUri,
    string ExceptionType,
    string Message,
    bool IsCanceled)
{
    /// <summary>
    /// Captures a failed call: the address is sanitized, the message passes the JSON redaction rules,
    /// and cancellation is recognized from either the token or the exception type.
    /// </summary>
    public static ProtoHttpFailureDiagnostics From(
        Uri? requestUri,
        Exception exception,
        CancellationToken cancellationToken,
        ProtoHttpAttachmentOptions? attachmentOptions)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(
            ProtoHttpDiagnosticSanitizer.SanitizeUri(requestUri, attachmentOptions),
            exception.GetType().FullName ?? exception.GetType().Name,
            ProtoHttpDiagnosticSanitizer.SanitizeBody(exception.Message, attachmentOptions),
            cancellationToken.IsCancellationRequested || exception is OperationCanceledException);
    }
}
