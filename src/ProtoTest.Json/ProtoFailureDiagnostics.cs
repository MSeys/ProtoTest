namespace ProtoTest.Json;

using ProtoTest.Core;

/// <summary>
/// The sanitized facts shared by every failed call in every protocol, so REST, GraphQL, gRPC and
/// messaging redact and report a failure the same way: the address and the message pass the shared
/// redaction rules, and cancellation is recognized from either the token or the exception type.
/// </summary>
public sealed record ProtoFailureDiagnostics(
    string? RequestUri,
    string ExceptionType,
    string Message,
    bool IsCanceled)
{
    /// <summary>How long the failed call ran, when the protocol measured it.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Captures a failed call. <paramref name="options"/> supplies the redaction rules;
    /// <paramref name="sensitiveQueryParameters"/> overrides the address redaction list, so an HTTP
    /// protocol keeps its configured <c>SensitiveQueryParameters</c> while gRPC and messaging use the
    /// shared defaults.
    /// </summary>
    public static ProtoFailureDiagnostics From(
        Uri? requestUri,
        Exception exception,
        CancellationToken cancellationToken,
        JsonDiagnosticOptions? options = null,
        IReadOnlyCollection<string>? sensitiveQueryParameters = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new(
            SanitizeUri(requestUri, options, sensitiveQueryParameters),
            exception.GetType().FullName ?? exception.GetType().Name,
            JsonDiagnosticSanitizer.Sanitize(exception.Message, options),
            cancellationToken.IsCancellationRequested || exception is OperationCanceledException);
    }

    // User-info is always removed; query values are redacted unless the caller turned redaction off.
    private static string? SanitizeUri(
        Uri? uri,
        JsonDiagnosticOptions? options,
        IReadOnlyCollection<string>? sensitiveQueryParameters)
    {
        if (uri is null)
        {
            return null;
        }

        return options?.RedactSensitiveData == false
            ? ProtoUriSanitizer.WithoutUserInfo(uri.OriginalString)
            : ProtoUriSanitizer.Sanitize(uri, sensitiveQueryParameters);
    }
}
