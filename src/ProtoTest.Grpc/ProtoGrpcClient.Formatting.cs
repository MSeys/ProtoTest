namespace ProtoTest.Grpc;

using System.Globalization;
using global::Google.Protobuf;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using ProtoTest.Core;
using ProtoTest.Grpc.Authentication;
using ProtoTest.Http;
using ProtoTest.Json;

public sealed partial class ProtoGrpcClient
{
    private static readonly JsonFormatter AttachmentFormatter = new(
        JsonFormatter.Settings.Default.WithFormatDefaultValues(true).WithFormatEnumsAsIntegers(false));
    private void CaptureSingle(
        ProtoTraceOperation operation,
        GrpcAttachmentOptions options,
        string attachmentName,
        object message,
        string description)
        => ProtoAttachmentCapture.TryAdd(
            _context,
            operation,
            AttachmentFailure,
            attachmentName,
            () => SanitizeForAttachment(options, FormatMessage(message)),
            "application/json",
            description);

    private void CaptureStream(
        ProtoTraceOperation operation,
        GrpcAttachmentOptions options,
        string attachmentName,
        IEnumerable<object> messages,
        string description)
        => ProtoAttachmentCapture.TryAdd(
            _context,
            operation,
            AttachmentFailure,
            attachmentName,
            () => SanitizeForAttachment(
                options,
                $"[{string.Join(",", messages.Take(MaxCapturedStreamMessages).Select(FormatMessage))}]"),
            "application/json",
            description);

    /// <summary>
    /// The attachment name includes the sanitized client name, so two clients calling the same method in
    /// one test do not collide on the same attachment name.
    /// </summary>
    private static string AttachmentName(string targetName, IMethod method, string direction, int callNumber)
        => $"grpc-{SanitizeName(targetName)}-{method.ServiceName}-{method.Name}-{direction}-" +
           callNumber.ToString(CultureInfo.InvariantCulture);

    private static string SanitizeName(string value)
    {
        var sanitized = new string([.. value.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-')]);
        return string.IsNullOrWhiteSpace(sanitized) ? "client" : sanitized;
    }

    private static string SanitizeForAttachment(GrpcAttachmentOptions options, string json)
        => ProtoTraceContent.Preview(
            JsonDiagnosticSanitizer.Sanitize(json, options, truncate: false),
            options.MaxDiagnosticBodyLength) ?? string.Empty;

    private static string FormatMessage(object value)
        => value is IMessage message
            ? AttachmentFormatter.Format(message)
            : JsonDiagnosticSanitizer.Serialize(value);

    private static ProtoTraceSection ResponseSection(object? response)
        => new("Response", ProtoTraceSectionKind.Code, Content: Format(response), Language: "protobuf");

    private static string? Format(object? value)
        => ProtoTraceContent.Preview(FormatRaw(value));

    private static string? FormatRaw(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                string text => text,
                IEnumerable<object> items => string.Join(
                    Environment.NewLine,
                    items.Select(item => FormatRaw(item) ?? string.Empty)),
                _ => value.ToString()
            };
        }
        catch (Exception exception)
        {
            return $"[unavailable: {value?.GetType().FullName} ({exception.GetType().Name})]";
        }
    }
}
