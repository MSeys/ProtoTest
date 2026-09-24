namespace ProtoTest.Json;

using System.Collections;
using System.Globalization;
using System.Text.Json;
using ProtoTest.Core;

/// <summary>
/// The context of one traced shape assertion: where it records, under which parent, with which extra
/// attributes, and whether the expected shape is captured as an attachment.
/// </summary>
public sealed record ProtoShapeAssertionContext(
    ProtoExecutionContext? Execution,
    string Source,
    string Title,
    string? ParentOperationId = null,
    IReadOnlyDictionary<string, string?>? ExtraAttributes = null,
    bool CaptureExpectedShape = false,
    string? AttachmentName = null,
    string? AttachmentDescription = null);

/// <summary>
/// The one traced shape assertion REST, GraphQL, gRPC and messaging share. It records the
/// <c>assert.json.shape</c> operation with the expected and actual JSON, the matched properties or every
/// mismatch, captures the expected shape when the protocol asks for it, and fails the operation before
/// rethrowing, so the evidence is in the trace even when the test fails.
/// </summary>
public static class ProtoShapeAssertion
{
    /// <summary>
    /// Matches <paramref name="actualJson"/> against <paramref name="expectedShape"/> and records the
    /// assertion on <see cref="ProtoShapeAssertionContext.Execution"/>. A null execution still matches
    /// and throws, untraced. A null <paramref name="expectedShape"/> expects JSON null, matching the
    /// matcher's documented null expectations. On success <paramref name="observation"/> builds the
    /// protocol's observation from the matched properties; returning null records none. A mismatch
    /// fails the operation and rethrows the shape exception. Returns the matched property paths.
    /// </summary>
    public static IReadOnlyList<string> Assert(
        ProtoShapeAssertionContext context,
        string? actualJson,
        object? expectedShape,
        JsonSerializerOptions? options = null,
        JsonDiagnosticOptions? diagnosticOptions = null,
        Func<IReadOnlyList<string>, ProtoObservation?>? observation = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var operation = context.Execution is null
            ? null
            : context.Execution.Trace
                .Operation("assert.json.shape", context.Title, context.Source)
                .With("expected.type", expectedShape?.GetType().FullName)
                .With(context.ExtraAttributes)
                .Parent(context.ParentOperationId)
                .Begin();

        try
        {
            // Describing the expected shape can itself fail (a hostile or cyclic shape); it must run
            // inside the traced operation so the failure is recorded rather than escaping untraced.
            var expectedShapeJson = JsonDiagnosticSanitizer.Serialize(DescribeExpectedValue(expectedShape, options), diagnosticOptions);
            var actualShapeJson = actualJson is null
                ? null
                : JsonDiagnosticSanitizer.Sanitize(actualJson, diagnosticOptions);
            operation?.SetAttribute("shape.expected", expectedShapeJson);
            operation?.SetAttribute("shape.actual", actualShapeJson);

            if (context.CaptureExpectedShape && context.Execution is not null && context.AttachmentName is { Length: > 0 } attachmentName)
            {
                context.Execution.AddAttachment(
                    attachmentName,
                    expectedShapeJson,
                    "application/json",
                    context.AttachmentDescription);
            }

            var matchedProperties = JsonShapeMatcher.AssertMatch(actualJson ?? string.Empty, expectedShape, options);
            operation?.SetAttribute("matched.property_count", matchedProperties.Count.ToString());
            operation?.SetAttribute("matched.properties", string.Join(", ", matchedProperties));
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(matchedProperties, diagnosticOptions));
            operation?.SetAttribute("shape.result", "matched");
            operation?.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [
                    new(
                        "shape",
                        $"{matchedProperties.Count} {(matchedProperties.Count == 1 ? "property" : "properties")}",
                        Tone: ProtoTraceSectionTone.Success)
                ]));
            if (context.Execution is not null && observation?.Invoke(matchedProperties) is { } recorded)
            {
                context.Execution.RecordObservation(recorded);
            }

            operation?.Succeed();
            return matchedProperties;
        }
        catch (JsonShapeMismatchException exception)
        {
            operation?.SetAttribute("shape.result", "mismatched");
            operation?.SetAttribute("shape.matches", JsonDiagnosticSanitizer.Serialize(exception.MatchedProperties, diagnosticOptions));
            operation?.SetAttribute("shape.mismatches", SerializeMismatches(exception.Mismatches, options, diagnosticOptions));
            operation?.SetAttribute("shape.mismatch_count", exception.Mismatches.Count.ToString());
            operation?.AddSection(new ProtoTraceSection(
                "Result",
                ProtoTraceSectionKind.Checks,
                [
                    new(
                        "shape",
                        $"{exception.Mismatches.Count} {(exception.Mismatches.Count == 1 ? "mismatch" : "mismatches")}",
                        exception.Message,
                        ProtoTraceSectionTone.Error)
                ]));
            operation?.Fail(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation?.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Records the assertion that data was present at all, for a protocol whose response can legitimately
    /// carry none. The operation, attributes and Checks section mirror a mismatch, and the protocol
    /// supplies the failure its own exception type and message.
    /// </summary>
    public static void AssertMissing(
        ProtoShapeAssertionContext context,
        object? expectedShape,
        Func<Exception> failureFactory)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(failureFactory);

        using var operation = context.Execution is null
            ? null
            : context.Execution.Trace
                .Operation("assert.json.shape", context.Title, context.Source)
                .With("expected.type", expectedShape?.GetType().FullName)
                .With(context.ExtraAttributes)
                .Parent(context.ParentOperationId)
                .Begin();
        var exception = failureFactory();
        operation?.SetAttribute("shape.result", "mismatched");
        operation?.SetAttribute("shape.mismatch_count", "1");
        operation?.AddSection(new ProtoTraceSection(
            "Result",
            ProtoTraceSectionKind.Checks,
            [
                new(
                    "shape",
                    "no data",
                    exception.Message,
                    ProtoTraceSectionTone.Error)
            ]));
        operation?.Fail(exception);
        throw exception;
    }

    /// <summary>
    /// Projects the mismatches for the trace. The expected side of a mismatch can hold a value
    /// constraint or any other object System.Text.Json cannot serialize, so each expected value is
    /// described through the same expansion the recorded shape uses; the actual side is already a
    /// JSON-safe scalar or raw text.
    /// </summary>
    private static string SerializeMismatches(
        IReadOnlyList<JsonShapeMismatch> mismatches,
        JsonSerializerOptions? options,
        JsonDiagnosticOptions? diagnosticOptions)
        => JsonDiagnosticSanitizer.Serialize(
            mismatches.Select(mismatch => new
            {
                mismatch.PropertyPath,
                mismatch.Reason,
                Expected = DescribeExpectedValue(mismatch.Expected, options),
                mismatch.Actual
            }).ToArray(),
            diagnosticOptions);

    /// <summary>The maximum nesting depth described before a placeholder is recorded.</summary>
    private const int MaxDescriptionDepth = 16;

    /// <summary>
    /// The maximum number of containers expanded in one description. The depth cap alone cannot bound a
    /// cyclic or diamond shape: an object with four self-references still expands 4^depth times, so the
    /// whole description gets a budget and stops with the same depth-limit placeholder.
    /// </summary>
    private const int MaxDescriptionNodes = 4096;

    /// <summary>
    /// Expands a shape into a serializable value: value constraints become their description, and nested
    /// objects and arrays are expanded so the trace records the shape the test actually declared. The
    /// expansion follows the matcher's own rules - the same scalar types, the wire names naming policy
    /// and <c>[JsonPropertyName]</c> give, and <c>[JsonIgnore]</c> properties left out - so the recorded
    /// expected shape is the shape the assertion compared. Dictionaries are described as objects, the
    /// depth is capped, and the total number of expanded containers is capped, so cyclic or
    /// pathologically deep shapes cannot exhaust the stack or hang.
    /// </summary>
    private static object? DescribeExpectedValue(object? expected, JsonSerializerOptions? options)
    {
        var budget = MaxDescriptionNodes;
        return DescribeExpectedValue(expected, depth: 0, ref budget, options);
    }

    private static object? DescribeExpectedValue(object? expected, int depth, ref int budget, JsonSerializerOptions? options)
    {
        if (expected is null)
        {
            return null;
        }

        if (expected is IJsonValueMatcher matcher)
        {
            return $"constraint: {matcher.Description}";
        }

        if (depth >= MaxDescriptionDepth)
        {
            return $"<{expected.GetType().Name} at depth limit>";
        }

        var type = expected.GetType();
        if (JsonScalarTypes.IsScalar(type))
        {
            return expected;
        }

        if (budget-- <= 0)
        {
            return $"<{type.Name} at depth limit>";
        }

        // A dictionary is an object shape, not a sequence of its key/value pairs. Keys are stringified,
        // matching the matcher: JSON object names are always strings.
        if (expected is IDictionary dictionary)
        {
            var described = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                described[Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty] = DescribeExpectedValue(entry.Value, depth + 1, ref budget, options);
            }

            return described;
        }

        if (expected is IEnumerable values)
        {
            var described = new List<object?>();
            foreach (var item in values)
            {
                described.Add(DescribeExpectedValue(item, depth + 1, ref budget, options));
            }

            return described.ToArray();
        }

        // The matcher's own property rules: wire names from [JsonPropertyName] or the naming policy,
        // and [JsonIgnore] properties left out, so what the trace shows is what the assertion compared.
        if (JsonShapeMatcher.TryProperties(expected, options, out var properties))
        {
            var describedProperties = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (name, value) in properties)
            {
                describedProperties[name] = DescribeExpectedValue(value, depth + 1, ref budget, options);
            }

            return describedProperties;
        }

        return expected;
    }
}
